using AgentHub.Contracts.DTOs.Conversation;
using AgentHub.Contracts.Responses;
using AgentHub.Core.Domain.Enums;
using AgentHub.Core.Domain.Models;
using AgentHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace AgentHub.API.Controllers;

/// <summary>
/// 会话管理控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationController : ControllerBase
{
    private readonly IConversationService _conversationService;
    private readonly ILogger<ConversationController> _logger;

    public ConversationController(IConversationService conversationService, ILogger<ConversationController> logger)
    {
        _conversationService = conversationService;
        _logger = logger;
    }

    /// <summary>
    /// 创建新会话
    /// </summary>
    [HttpPost("add")]
    public async Task<ActionResult<ApiResponse<ConversationOutput>>> AddConversation([FromBody] AddConversationInput input)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return BadRequest(ApiResponse<ConversationOutput>.ErrorResponse("请求参数验证失败", errors));
            }

            var userId = GetCurrentUserId();

            // 解析Agent类型
            if (!Enum.TryParse<AgentType>(input.AgentType, out var agentType))
            {
                return BadRequest(ApiResponse<ConversationOutput>.ErrorResponse($"无效的Agent类型: {input.AgentType}"));
            }

            var conversation = await _conversationService.CreateConversationAsync(userId, agentType, input.Title);

            var output = new ConversationOutput
            {
                Id = conversation.Id,
                Title = conversation.Title ?? string.Empty,
                AgentType = conversation.AgentType.ToString(),
                Status = conversation.Status.ToString(),
                MessageCount = 0,
                CreatedAt = conversation.StartedAt,
                UpdatedAt = conversation.LastMessageAt ?? conversation.StartedAt
            };

            return Ok(ApiResponse<ConversationOutput>.SuccessResponse(output, "创建会话成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建会话失败");
            return StatusCode(500, ApiResponse<ConversationOutput>.ErrorResponse("创建会话失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 分页获取会话列表
    /// </summary>
    [HttpGet("page")]
    public async Task<ActionResult<ApiResponse<object>>> GetConversationPage([FromQuery] QueryPageConversationInput input)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return BadRequest(ApiResponse<object>.ErrorResponse("请求参数验证失败", errors));
            }

            var userId = GetCurrentUserId();

            // 解析Agent类型筛选
            AgentType? agentType = null;
            if (!string.IsNullOrEmpty(input.AgentType) && Enum.TryParse<AgentType>(input.AgentType, out var type))
            {
                agentType = type;
            }

            var (conversations, total) = await _conversationService.GetUserConversationsAsync(
                userId,
                input.PageIndex,
                input.PageSize,
                agentType);

            var outputs = conversations.Select(c => new ConversationOutput
            {
                Id = c.Id,
                Title = c.Title ?? string.Empty,
                AgentType = c.AgentType.ToString(),
                Status = c.Status.ToString(),
                MessageCount = c.Messages?.Count ?? 0,
                CreatedAt = c.StartedAt,
                UpdatedAt = c.LastMessageAt ?? c.StartedAt
            }).ToList();

            var result = new
            {
                List = outputs,
                Total = total,
                PageIndex = input.PageIndex,
                PageSize = input.PageSize,
                TotalPages = (int)Math.Ceiling(total / (double)input.PageSize)
            };

            return Ok(ApiResponse<object>.SuccessResponse(result, "获取成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取会话列表失败");
            return StatusCode(500, ApiResponse<object>.ErrorResponse("获取会话列表失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取会话详情（含消息列表）
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ConversationDetailOutput>>> GetConversationDetail(long id)
    {
        try
        {
            var userId = GetCurrentUserId();

            var conversation = await _conversationService.GetConversationWithMessagesAsync(id, userId);

            if (conversation == null)
            {
                return NotFound(ApiResponse<ConversationDetailOutput>.ErrorResponse("会话不存在或无权访问"));
            }

            var output = new ConversationDetailOutput
            {
                Id = conversation.Id,
                Title = conversation.Title ?? string.Empty,
                AgentType = conversation.AgentType.ToString(),
                Status = conversation.Status.ToString(),
                MessageCount = conversation.Messages?.Count ?? 0,
                CreatedAt = conversation.StartedAt,
                UpdatedAt = conversation.LastMessageAt ?? conversation.StartedAt,
                Messages = conversation.Messages?.Select(m =>
                {
                    // 反序列化工具调用历史
                    List<ToolCallHistoryItemOutput>? toolCallHistory = null;
                    if (!string.IsNullOrEmpty(m.MetadataJson))
                    {
                        try
                        {
                            var metadata = JsonSerializer.Deserialize<MessageMetadata>(m.MetadataJson);
                            if (metadata?.ToolCallHistory != null && metadata.ToolCallHistory.Count > 0)
                            {
                                toolCallHistory = metadata.ToolCallHistory.Select(t => new ToolCallHistoryItemOutput
                                {
                                    Timestamp = t.Timestamp,
                                    Message = t.Message
                                }).ToList();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"反序列化消息元数据失败: MessageId={m.Id}");
                        }
                    }

                    return new MessageOutput
                    {
                        Id = m.Id,
                        Role = m.Role.ToString(),
                        Content = m.Content,
                        CreatedAt = m.CreatedAt,
                        ToolCallHistory = toolCallHistory
                    };
                }).ToList() ?? new List<MessageOutput>()
            };

            return Ok(ApiResponse<ConversationDetailOutput>.SuccessResponse(output, "获取成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取会话详情失败: ConversationId={id}");
            return StatusCode(500, ApiResponse<ConversationDetailOutput>.ErrorResponse("获取会话详情失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 更新会话标题
    /// </summary>
    [HttpPut("update")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateConversation([FromBody] UpdateConversationInput input)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return BadRequest(ApiResponse<bool>.ErrorResponse("请求参数验证失败", errors));
            }

            var userId = GetCurrentUserId();

            var success = await _conversationService.UpdateConversationTitleAsync(input.ConversationId, input.Title, userId);

            if (!success)
            {
                return NotFound(ApiResponse<bool>.ErrorResponse("会话不存在或无权访问"));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "更新成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新会话标题失败");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse("更新失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 删除会话
    /// </summary>
    [HttpDelete("delete/{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteConversation(long id)
    {
        try
        {
            var userId = GetCurrentUserId();

            var success = await _conversationService.DeleteConversationAsync(id, userId);

            if (!success)
            {
                return NotFound(ApiResponse<bool>.ErrorResponse("会话不存在或无权访问"));
            }

            return Ok(ApiResponse<bool>.SuccessResponse(true, "删除成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除会话失败: ConversationId={id}");
            return StatusCode(500, ApiResponse<bool>.ErrorResponse("删除失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取当前登录用户ID
    /// </summary>
    private long GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("无法获取用户ID");
        }
        return userId;
    }
}
