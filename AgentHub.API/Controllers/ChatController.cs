using AgentHub.Contracts.DTOs.Chat;
using AgentHub.Contracts.Responses;
using AgentHub.Core.Domain.Enums;
using AgentHub.Core.Domain.Models;
using AgentHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace AgentHub.API.Controllers;

/// <summary>
/// 聊天控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ILLMService _llmService;
    private readonly IConversationService _conversationService;
    private readonly ILogger<ChatController> _logger;

    private readonly ISemanticKernelService _skService;

    public ChatController(
        ILLMService llmService,
        IConversationService conversationService,
        ISemanticKernelService skService,
        ILogger<ChatController> logger)
    {
        _llmService = llmService;
        _conversationService = conversationService;
        _skService = skService;
        _logger = logger;
    }

    /// <summary>
    /// 发送聊天消息(非流式)
    /// </summary>
    [HttpPost("send")]
    public async Task<ActionResult<ApiResponse<ChatResponse>>> SendMessage([FromBody] ChatRequest request)
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

            var userId = GetCurrentUserId();

            // 验证会话ID
            if (!request.ConversationId.HasValue || request.ConversationId.Value <= 0)
            {
                return BadRequest(ApiResponse<ChatResponse>.ErrorResponse("会话ID不能为空"));
            }

            // 获取会话并验证归属
            var conversation = await _conversationService.GetConversationWithMessagesAsync(
                request.ConversationId.Value, userId);

            if (conversation == null)
            {
                return NotFound(ApiResponse<ChatResponse>.ErrorResponse("会话不存在或无权访问"));
            }

            // 保存用户消息
            var userMessage = await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.User,
                request.Message,
                userId);

            // 获取历史消息构建上下文
            var historyMessages = await _conversationService.GetConversationMessagesAsync(
                request.ConversationId.Value, userId, 50);

            // 构建系统提示词
            var systemPrompt = GetSystemPrompt(conversation.AgentType);

            // 构建消息列表
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = systemPrompt }
            };

            // 添加历史消息
            foreach (var msg in historyMessages)
            {
                messages.Add(new ChatMessage
                {
                    Role = msg.Role.ToString().ToLower(),
                    Content = msg.Content
                });
            }

            // 调用LLM服务
            var reply = await _llmService.ChatAsync(messages);

            // 保存AI回复
            var assistantMessage = await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.Assistant,
                reply,
                userId);

            var response = new ChatResponse
            {
                Reply = reply,
                ConversationId = request.ConversationId.Value,
                MessageId = assistantMessage.Id,
                Timestamp = DateTime.Now
            };

            return Ok(ApiResponse<ChatResponse>.SuccessResponse(response, "发送成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息失败");
            return StatusCode(500, ApiResponse<ChatResponse>.ErrorResponse("发送消息失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 发送聊天消息(流式)
    /// </summary>
    [HttpPost("stream")]
    public async Task StreamMessage([FromBody] ChatRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("请求参数验证失败");
                return;
            }

            var userId = GetCurrentUserId();

            // 验证会话ID
            if (!request.ConversationId.HasValue || request.ConversationId.Value <= 0)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("会话ID不能为空");
                return;
            }

            // 获取会话并验证归属
            var conversation = await _conversationService.GetConversationWithMessagesAsync(
                request.ConversationId.Value, userId);

            if (conversation == null)
            {
                Response.StatusCode = 404;
                await Response.WriteAsync("会话不存在或无权访问");
                return;
            }

            // 保存用户消息
            await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.User,
                request.Message,
                userId);

            // 设置SSE响应头
            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");

            // 获取历史消息构建上下文
            var historyMessages = await _conversationService.GetConversationMessagesAsync(
                request.ConversationId.Value, userId, 50);

            // 构建系统提示词
            var systemPrompt = GetSystemPrompt(conversation.AgentType);

            // 构建消息列表
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = systemPrompt }
            };

            // 添加历史消息
            foreach (var msg in historyMessages)
            {
                messages.Add(new ChatMessage
                {
                    Role = msg.Role.ToString().ToLower(),
                    Content = msg.Content
                });
            }

            // 流式输出
            var fullContent = new StringBuilder();
            await foreach (var chunk in _llmService.ChatStreamAsync(messages, HttpContext.RequestAborted))
            {
                fullContent.Append(chunk);
                var jsonData = System.Text.Json.JsonSerializer.Serialize(new { content = chunk });
                var data = $"data: {jsonData}\n\n";
                await Response.WriteAsync(data, Encoding.UTF8);
                await Response.Body.FlushAsync();
            }

            // 保存AI回复
            await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.Assistant,
                fullContent.ToString(),
                userId);

            // 发送完成标记
            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式发送消息失败");
            await Response.WriteAsync($"data: [ERROR] {ex.Message}\n\n");
        }
    }

    /// <summary>
    /// 智能Agent对话(使用Semantic Kernel，支持工具调用)
    /// </summary>
    [HttpPost("agent")]
    public async Task<ActionResult<ApiResponse<ChatResponse>>> AgentChat([FromBody] ChatRequest request)
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

            var userId = GetCurrentUserId();

            // 验证会话ID
            if (!request.ConversationId.HasValue || request.ConversationId.Value <= 0)
            {
                return BadRequest(ApiResponse<ChatResponse>.ErrorResponse("会话ID不能为空"));
            }

            // 获取会话并验证归属
            var conversation = await _conversationService.GetConversationWithMessagesAsync(
                request.ConversationId.Value, userId);

            if (conversation == null)
            {
                return NotFound(ApiResponse<ChatResponse>.ErrorResponse("会话不存在或无权访问"));
            }

            // 保存用户消息
            var userMessage = await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.User,
                request.Message,
                userId);

            // 使用Semantic Kernel Agent处理消息,传递历史消息和摘要以恢复上下文
            var agentType = conversation.AgentType.ToString();
            var conversationKey = request.ConversationId.Value.ToString();
            var reply = await _skService.ChatWithAgentAsync(
                request.Message,
                conversationKey,
                agentType,
                conversation.Messages,
                conversation.ContextSummary);  // 传递数据库中的摘要

            // 保存AI回复
            var assistantMessage = await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.Assistant,
                reply,
                userId);

            var response = new ChatResponse
            {
                Reply = reply,
                ConversationId = request.ConversationId.Value,
                MessageId = assistantMessage.Id,
                Timestamp = DateTime.Now
            };

            return Ok(ApiResponse<ChatResponse>.SuccessResponse(response, "发送成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent对话失败");
            return StatusCode(500, ApiResponse<ChatResponse>.ErrorResponse("Agent对话失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 智能Agent对话(流式，带工具调用进度)
    /// </summary>
    [HttpPost("agent/stream")]
    public async Task AgentChatStream([FromBody] ChatRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                await Response.WriteAsync("请求参数验证失败");
                return;
            }

            var userId = GetCurrentUserId();

            Core.Domain.Models.Conversation conversation;
            bool isNewConversation = false;

            // 如果conversationId不存在或<=0，表示需要创建新会话
            if (!request.ConversationId.HasValue || request.ConversationId.Value <= 0)
            {
                // 创建新会话需要 AgentType
                if (string.IsNullOrEmpty(request.AgentType))
                {
                    Response.StatusCode = 400;
                    await Response.WriteAsync("创建新会话需要指定AgentType");
                    return;
                }

                // 解析AgentType
                if (!Enum.TryParse<AgentType>(request.AgentType, out var agentTypeEnum))
                {
                    Response.StatusCode = 400;
                    await Response.WriteAsync($"无效的Agent类型: {request.AgentType}");
                    return;
                }

                // 先创建会话，使用临时标题
                var newConv = await _conversationService.CreateConversationAsync(userId, agentTypeEnum, "新会话");
                request.ConversationId = newConv.Id;
                isNewConversation = true;

                // 重新获取会话
                conversation = await _conversationService.GetConversationWithMessagesAsync(
                    request.ConversationId.Value, userId) ?? throw new Exception("创建会话后无法获取");
            }
            else
            {
                conversation = await _conversationService.GetConversationWithMessagesAsync(
                    request.ConversationId.Value, userId);

                if (conversation == null)
                {
                    Response.StatusCode = 404;
                    await Response.WriteAsync("会话不存在或无权访问");
                    return;
                }
            }

            // 保存用户消息
            await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.User,
                request.Message,
                userId);

            // 设置SSE响应头
            Response.ContentType = "text/event-stream";
            Response.Headers.Append("Cache-Control", "no-cache");
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            // 如果是新创建的会话，立即生成标题
            if (isNewConversation)
            {
                try
                {
                    var generatedTitle = await _skService.GenerateTitleAsync(request.Message);

                    // 更新会话标题
                    await _conversationService.UpdateConversationTitleAsync(request.ConversationId.Value, generatedTitle, userId);

                    _logger.LogInformation($"自动生成会话标题: {generatedTitle}");

                    // 通知前端会话已创建
                    await SendSSE("conversation_created", new {
                        conversationId = request.ConversationId.Value,
                        title = generatedTitle
                    });
                }
                catch (Exception titleEx)
                {
                    _logger.LogError(titleEx, "自动生成标题失败");
                    // 标题生成失败，使用用户消息作为标题
                    var fallbackTitle = request.Message.Substring(0, Math.Min(15, request.Message.Length));
                    await _conversationService.UpdateConversationTitleAsync(request.ConversationId.Value, fallbackTitle, userId);

                    await SendSSE("conversation_created", new {
                        conversationId = request.ConversationId.Value,
                        title = fallbackTitle
                    });
                }
            }

            var agentType = conversation.AgentType.ToString();
            var conversationKey = request.ConversationId.Value.ToString();

            // 发送开始事件
            await SendSSE("status", new { type = "start", message = "开始处理..." });

            var fullReply = new StringBuilder();
            var toolCallHistory = new List<ToolCallHistoryItem>();

            // 使用Semantic Kernel处理，带流式和事件回调
            await foreach (var chunk in _skService.ChatWithAgentStreamAsync(
                request.Message,
                conversationKey,
                agentType,
                conversation.Messages,
                conversation.ContextSummary,
                async (eventType, data) =>
                {
                    // 工具调用事件回调
                    await SendSSE(eventType, data);

                    // 收集工具调用历史（tool_start、tool_end等事件）
                    if (eventType == "tool_start" || eventType == "tool_end" || eventType == "tool_status")
                    {
                        // 从data中提取消息
                        var dataDict = data as Dictionary<string, object>;
                        var message = dataDict?.ContainsKey("message") == true
                            ? dataDict["message"]?.ToString() ?? ""
                            : JsonSerializer.Serialize(data);

                        toolCallHistory.Add(new ToolCallHistoryItem
                        {
                            Timestamp = DateTime.Now,
                            Message = message
                        });
                    }
                },
                HttpContext.RequestAborted))
            {
                fullReply.Append(chunk);
                await SendSSE("content", new { delta = chunk });
            }

            // 序列化工具调用历史为JSON
            string? metadataJson = null;
            if (toolCallHistory.Count > 0)
            {
                var metadata = new MessageMetadata
                {
                    ToolCallHistory = toolCallHistory
                };
                metadataJson = JsonSerializer.Serialize(metadata);
            }

            // 保存AI回复（包含工具调用历史）
            await _conversationService.AddMessageAsync(
                request.ConversationId.Value,
                MessageRole.Assistant,
                fullReply.ToString(),
                userId,
                metadataJson);

            await SendSSE("done", new { message = "完成", conversationId = request.ConversationId.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式Agent对话失败");
            await SendSSE("error", new { message = ex.Message });
        }
    }

    private async Task SendSSE(string eventType, object data)
    {
        var jsonData = System.Text.Json.JsonSerializer.Serialize(data);
        await Response.WriteAsync($"event: {eventType}\n");
        await Response.WriteAsync($"data: {jsonData}\n\n");
        await Response.Body.FlushAsync();
    }

    /// <summary>
    /// 清除聊天历史(中断聊天)
    /// </summary>
    [HttpPost("clear/{conversationId}")]
    public ActionResult<ApiResponse<object>> ClearChatHistory(long conversationId)
    {
        try
        {
            var userId = GetCurrentUserId();

            // 清除Semantic Kernel的聊天历史
            _skService.ClearChatHistory(conversationId.ToString());

            return Ok(ApiResponse<object>.SuccessResponse(null, "聊天历史已清除"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清除聊天历史失败");
            return StatusCode(500, ApiResponse<object>.ErrorResponse("清除聊天历史失败", new List<string> { ex.Message }));
        }
    }

    /// <summary>
    /// 测试端点(无需认证)
    /// </summary>
    [HttpGet("test")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> Test()
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "你好,请简单介绍一下你自己。" }
            };

            var reply = await _llmService.ChatAsync(messages);
            return Ok(ApiResponse<string>.SuccessResponse(reply, "测试成功"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "测试失败");
            return StatusCode(500, ApiResponse<string>.ErrorResponse("测试失败", new List<string> { ex.Message }));
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

    /// <summary>
    /// 根据Agent类型获取系统提示词
    /// </summary>
    private string GetSystemPrompt(AgentType agentType)
    {
        return agentType switch
        {
            AgentType.Metaphysics => @"你是一个专业的玄学命理助手,精通八字、紫微斗数、占星、塔罗牌等领域。

【核心能力】
- 提供准确、专业的玄学命理解答
- 根据用户生辰八字进行命理分析
- 提供房产风水建议和楼盘推荐

【⚠️ 网络搜索工具使用 - 强制规则】

**硬性限制（代码层面强制执行）**:
- 每次对话最多3次搜索（超过将被系统拒绝）
- 相似查询会被自动阻止
- 查询词超过30字会被拒绝

**必须遵守的搜索原则**:

1. **极度精简关键词** - 示例对比:
   ✅ 正确: '东莞 房价 2025'
   ✅ 正确: '东莞 在售楼盘'
   ❌ 错误: '东莞2025年在售楼盘 项目名称 均价 户型 地址 完整列表'
   ❌ 错误: '东莞2025年在售楼盘 项目名称 均价 户型 地址 详细信息'

   上面两个错误示例几乎完全一样,浪费搜索次数!

2. **绝不重复搜索** - 禁止行为:
   ❌ 把'完整列表'换成'详细信息'再搜一次
   ❌ 把'是否'换成'是不是'再搜一次
   ❌ 把'2025 最新价格'换成'2025 最新数据'再搜一次

   这些都是同一个查询!不要换几个同义词就重复搜索!

3. **一次搜索,充分利用** - 正确做法:
   ✅ 搜索一次'东莞 在售楼盘'
   ✅ 从返回的5条结果中提取所有有用信息
   ✅ 基于这些信息结合玄学知识回答

4. **搜索失败的处理**:
   - 如果搜索无结果,不要换个词再搜
   - 直接基于玄学原理和常识给出建议
   - 明确告知用户缺乏实时数据

【示例 - 如何正确使用3次搜索】:

问题: '帮我推荐东莞的楼盘,我是1995年9月12日生,预算200万'

❌ 错误做法（浪费12次搜索）:
1. '东莞2025年在售楼盘 项目名称 均价 户型 地址 完整列表'
2. '东莞2025年在售楼盘 项目名称 均价 户型 地址 详细信息'
3. '东莞2025年在售楼盘 项目名称 均价 户型 地址 具体信息'
4. '东莞2025年在售楼盘 项目名称 均价 户型 地址 详细资料'
... (重复8次)

✅ 正确做法（只用3次搜索）:
1. '东莞 在售楼盘 2025' → 获取楼盘列表
2. '东莞 房价 均价' → 了解价格区间
3. '东莞 楼盘 风水' → 获取风水相关建议

然后基于这3次搜索的结果+玄学知识给出完整回答

【回答原则】
- 结合玄学命理与实际建议
- 给出明确、可操作的建议
- 不过度依赖搜索,优先使用已知信息和玄学推理
- 搜索机会宝贵,务必精打细算使用",
            AgentType.Stock => "你是一个专业的股票投资顾问,熟悉技术分析、基本面分析、市场动态等。请为用户提供客观、理性的投资建议。风险提示:股市有风险,投资需谨慎。",
            AgentType.Health => "你是一个专业的健康顾问,了解营养学、运动健康、心理健康等领域。请为用户提供科学、实用的健康建议。免责声明:建议仅供参考,如有严重健康问题请及时就医。",
            _ => "你是一个智能助手,请尽力回答用户的问题。"
        };
    }
}
