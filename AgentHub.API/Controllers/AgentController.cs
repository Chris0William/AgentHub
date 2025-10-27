using AgentHub.Contracts.DTOs.Chat;
using AgentHub.Contracts.Responses;
using AgentHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentHub.API.Controllers;

/// <summary>
/// Agent控制器 - 使用Semantic Kernel
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    private readonly ISemanticKernelService _skService;
    private readonly ILogger<AgentController> _logger;

    public AgentController(ISemanticKernelService skService, ILogger<AgentController> logger)
    {
        _skService = skService;
        _logger = logger;
    }

    /// <summary>
    /// 与玄学Agent对话
    /// </summary>
    [HttpPost("metaphysics")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<ChatResponse>>> ChatWithMetaphysicsAgent([FromBody] ChatRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();
                return BadRequest(ApiResponse<ChatResponse>.ErrorResponse("请求参数验证失败", errors));
            }

            var reply = await _skService.ChatWithAgentAsync(request.Message, "Metaphysics");

            var response = new ChatResponse
            {
                Reply = reply,
                ConversationId = request.ConversationId ?? 0,
                MessageId = 0,
                Timestamp = DateTime.Now
            };

            return Ok(ApiResponse<ChatResponse>.SuccessResponse(response, "发送成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "与玄学Agent对话失败");
            return StatusCode(500, ApiResponse<ChatResponse>.ErrorResponse("对话失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 测试工具函数调用
    /// </summary>
    [HttpGet("test-tools")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> TestTools()
    {
        try
        {
            // 测试多个工具函数的自动调用
            var testMessages = new[]
            {
                "我是1990年出生的,我的生肖是什么?",
                "6月15日出生的人是什么星座?",
                "请帮我查看今天的宜忌",
                "甲木的五行属性是什么?",
                "2024年的干支纪年是什么?",
                "计算1995年3月20日出生的人的生命灵数"
            };

            var testMessage = testMessages[new Random().Next(testMessages.Length)];
            var reply = await _skService.ChatWithAgentAsync(testMessage, "Metaphysics");

            var result = $"测试消息: {testMessage}\n\nAgent回复: {reply}";

            return Ok(ApiResponse<string>.SuccessResponse(result, "测试成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "测试工具函数失败");
            return StatusCode(500, ApiResponse<string>.ErrorResponse("测试失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 获取Kernel信息
    /// </summary>
    [HttpGet("kernel-info")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<object>> GetKernelInfo()
    {
        try
        {
            var kernel = _skService.GetKernel();
            var plugins = kernel.Plugins.Select(p => new
            {
                Name = p.Name,
                Functions = p.Select(f => new
                {
                    Name = f.Name,
                    Description = f.Description
                }).ToList()
            }).ToList();

            var info = new
            {
                PluginCount = kernel.Plugins.Count,
                Plugins = plugins
            };

            return Ok(ApiResponse<object>.SuccessResponse(info, "获取成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取Kernel信息失败");
            return StatusCode(500, ApiResponse<object>.ErrorResponse("获取失败", new List<string> { ex.Message }));
        }
    }
}
