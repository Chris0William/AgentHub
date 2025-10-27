using AgentHub.Core.Interfaces;
using AgentHub.Infrastructure.AI.Plugins;
using AgentHub.Infrastructure.ExternalApis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Runtime.CompilerServices;

namespace AgentHub.Infrastructure.AI;

/// <summary>
/// Semantic Kernel服务实现
/// </summary>
public class SemanticKernelService : ISemanticKernelService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly ILogger<SemanticKernelService> _logger;
    private readonly Dictionary<string, ChatHistory> _chatHistories = new();

    // 并发控制：为每个会话创建独立的锁，防止同一会话的并发请求导致数据竞争
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _conversationLocks = new();

    public SemanticKernelService(
        IConfiguration configuration,
        ILogger<SemanticKernelService> logger,
        IJuheCalendarService juheCalendarService,
        IJuheHoroscopeService juheHoroscopeService,
        IWebSearchService webSearchService,
        IRealEstateService realEstateService,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;

        // 获取千问配置
        var apiKey = configuration["AI:Qwen:ApiKey"] ?? throw new InvalidOperationException("千问API密钥未配置");
        var apiEndpoint = configuration["AI:Qwen:ApiEndpoint"] ?? "https://dashscope.aliyuncs.com/compatible-mode/v1";
        var model = configuration["AI:Qwen:Model"] ?? "qwen-max";
        _logger.LogInformation($"SK配置: Endpoint={apiEndpoint}, Model={model}");

        // 创建Kernel Builder
        var builder = Kernel.CreateBuilder();

        // 使用OpenAI兼容模式连接千问 (经过IFM项目验证,支持function calling)
        builder.AddOpenAIChatCompletion(
            modelId: model,
            apiKey: apiKey,
            endpoint: new Uri(apiEndpoint));

        // 添加日期时间插件
        builder.Plugins.AddFromObject(new DateTimePlugin(), "DateTime");

        // 添加玄学插件
        builder.Plugins.AddFromObject(new MetaphysicsPlugin(juheCalendarService, juheHoroscopeService), "Metaphysics");

        // 添加网络搜索插件
        var webSearchLogger = loggerFactory.CreateLogger<WebSearchPlugin>();
        builder.Plugins.AddFromObject(new WebSearchPlugin(webSearchService, webSearchLogger), "WebSearch");

        // 添加房产数据插件
        var realEstateLogger = loggerFactory.CreateLogger<RealEstatePlugin>();
        builder.Plugins.AddFromObject(new RealEstatePlugin(realEstateService, realEstateLogger), "RealEstate");

        // 构建Kernel
        _kernel = builder.Build();

        // 获取聊天补全服务
        _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();

        _logger.LogInformation("Semantic Kernel初始化成功,已加载日期时间、玄学、网络搜索和房产数据插件");
    }

    /// <summary>
    /// 获取Kernel实例
    /// </summary>
    public Kernel GetKernel()
    {
        return _kernel;
    }

    /// <summary>
    /// 聊天补全(非流式)
    /// </summary>
    public async Task<string> ChatCompletionAsync(ChatHistory chatHistory, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.1,  // 进一步降低温度，减少幻觉，更倾向于使用工具和提供准确信息
                MaxTokens = 3000,   // 增加token限制,支持更长的搜索结果
                // 使用新版本的FunctionChoiceBehavior自动调用函数
                // 通过系统提示词中的"搜索效率原则"指导AI控制搜索次数
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var result = await _chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                settings,
                _kernel,
                cancellationToken
            );

            return result.Content ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "聊天补全失败");
            throw;
        }
    }

    /// <summary>
    /// 聊天补全(流式)
    /// </summary>
    public async IAsyncEnumerable<string> ChatCompletionStreamAsync(
        ChatHistory chatHistory,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.1,  // 进一步降低温度，减少幻觉，更倾向于使用工具和提供准确信息
            MaxTokens = 3000,   // 增加token限制,支持更长的搜索结果
            // 使用新版本的FunctionChoiceBehavior自动调用函数
            // 通过系统提示词中的"搜索效率原则"指导AI控制搜索次数
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        await foreach (var content in _chatCompletionService.GetStreamingChatMessageContentsAsync(
            chatHistory,
            settings,
            _kernel,
            cancellationToken))
        {
            if (!string.IsNullOrEmpty(content.Content))
            {
                yield return content.Content;
            }
        }
    }

    /// <summary>
    /// 使用Agent进行对话
    /// </summary>
    public async Task<string> ChatWithAgentAsync(
        string userMessage,
        string conversationId,
        string agentType = "Metaphysics",
        IEnumerable<Core.Domain.Models.Message>? historyMessages = null,
        string? contextSummary = null,
        CancellationToken cancellationToken = default)
    {
        // 使用conversationId作为key管理聊天历史
        var historyKey = conversationId;

        // 获取或创建该会话的锁（每个会话独立的锁，避免不同会话相互阻塞）
        var semaphore = _conversationLocks.GetOrAdd(historyKey, _ => new SemaphoreSlim(1, 1));

        // 等待锁（确保同一会话的请求串行执行）
        await semaphore.WaitAsync(cancellationToken);

        try
        {

            // 获取或创建该会话的聊天历史
            if (!_chatHistories.ContainsKey(historyKey))
            {
                var chatHistory = new ChatHistory();

                // 根据Agent类型设置系统提示词
                var systemPrompt = GetSystemPrompt(agentType);

                // 优先使用数据库中的摘要(长期记忆)
                if (!string.IsNullOrEmpty(contextSummary))
                {
                    // 使用数据库中持久化的摘要
                    systemPrompt += $"\n\n## 之前的对话摘要(长期记忆):\n{contextSummary}\n";
                    _logger.LogInformation($"使用数据库摘要恢复长期记忆: ConversationId={conversationId}, 摘要长度={contextSummary.Length}字符");
                }
                else if (historyMessages != null && historyMessages.Any())
                {
                    // 数据库没有摘要，动态生成（兼容旧数据或首次对话）
                    var oldMessages = historyMessages
                        .OrderByDescending(m => m.CreatedAt)
                        .Skip(10) // 跳过最近5轮(10条消息)，增加短期记忆范围
                        .OrderBy(m => m.CreatedAt)
                        .ToList();

                    // 如果有更早的对话,生成摘要并添加到系统提示
                    if (oldMessages.Any())
                    {
                        var summarizedContext = await GenerateSummaryAsync(oldMessages, agentType, cancellationToken);
                        systemPrompt += $"\n\n## 之前的对话摘要(长期记忆):\n{summarizedContext}\n";
                        _logger.LogInformation($"动态生成历史摘要(数据库无摘要): ConversationId={conversationId}, 包含{oldMessages.Count}条消息");
                    }
                }

                chatHistory.AddSystemMessage(systemPrompt);

                // 如果提供了历史消息,从数据库恢复对话历史
                // 策略: 恢复最近5轮纯文本对话，增强短期记忆能力
                // 更早的上下文通过ContextSummary保留
                if (historyMessages != null && historyMessages.Any())
                {
                    // 取最近10条消息(5轮对话)，让AI有更多上下文
                    var recentMessages = historyMessages
                        .OrderByDescending(m => m.CreatedAt)
                        .Take(10)
                        .OrderBy(m => m.CreatedAt)
                        .ToList();

                    foreach (var msg in recentMessages)
                    {
                        if (msg.Role == Core.Domain.Enums.MessageRole.User)
                        {
                            chatHistory.AddUserMessage(msg.Content);
                        }
                        else if (msg.Role == Core.Domain.Enums.MessageRole.Assistant)
                        {
                            chatHistory.AddAssistantMessage(msg.Content);
                        }
                    }
                    _logger.LogInformation($"从数据库恢复了 {recentMessages.Count} 条历史消息到会话 {conversationId}");
                }

                _chatHistories[historyKey] = chatHistory;
            }

            var history = _chatHistories[historyKey];

            // 添加用户消息
            history.AddUserMessage(userMessage);

            // 执行聊天补全
            var response = await ChatCompletionAsync(history, cancellationToken);

            // 将助手回复添加到历史
            history.AddAssistantMessage(response);

            // 智能历史管理: 只计算真正的对话消息(User/Assistant),排除工具调用
            // 工具调用是中间过程,不应计入"历史对话轮数"
            var conversationMessageCount = history.Count(m =>
                m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User ||
                m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant);

            if (conversationMessageCount > 60) // 30轮对话，增加阈值让AI保留更多上下文
            {
                _logger.LogInformation(
                    $"会话{conversationId}对话轮数过多(User+Assistant={conversationMessageCount}条, " +
                    $"总ChatHistory={history.Count}条),执行压缩");

                // 保留系统消息 + 最近30轮对话(60条User/Assistant消息)
                // 工具调用消息会被自动保留（因为它们在最近的对话中）
                var systemMsg = history.First();
                var recentConversationMessages = history
                    .Where(m => m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User ||
                               m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant)
                    .TakeLast(40)
                    .ToList();

                // 找到最早保留消息的索引
                var firstKeptMessage = recentConversationMessages.First();
                var firstKeptIndex = history.ToList().FindIndex(m => m == firstKeptMessage);

                // 保留从该索引开始的所有消息（包括中间的tool消息）
                var messagesToKeep = history.Skip(firstKeptIndex).ToList();

                history.Clear();
                history.Add(systemMsg);
                foreach (var msg in messagesToKeep)
                {
                    history.Add(msg);
                }

                _logger.LogInformation(
                    $"ChatHistory压缩完成: 对话消息{conversationMessageCount}条 → " +
                    $"{history.Count(m => m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User || m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant)}条, " +
                    $"总ChatHistory {history.Count}条");
            }

            return response;
        }
        catch (Microsoft.SemanticKernel.HttpOperationException httpEx)
        {
            // HTTP操作异常（LLM API调用失败）
            var errorDetails = new
            {
                ConversationId = conversationId,
                AgentType = agentType,
                StatusCode = httpEx.StatusCode,
                ErrorMessage = httpEx.Message,
                ChatHistoryCount = _chatHistories.ContainsKey(historyKey) ? _chatHistories[historyKey].Count : 0,
                UserMessage = userMessage?.Length > 100 ? userMessage.Substring(0, 100) + "..." : userMessage,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogError(httpEx,
                "LLM API调用失败。\n" +
                "会话ID: {ConversationId}\n" +
                "Agent类型: {AgentType}\n" +
                "HTTP状态码: {StatusCode}\n" +
                "错误消息: {ErrorMessage}\n" +
                "ChatHistory消息数: {ChatHistoryCount}\n" +
                "用户消息: {UserMessage}\n" +
                "时间: {Timestamp}",
                errorDetails.ConversationId,
                errorDetails.AgentType,
                errorDetails.StatusCode,
                errorDetails.ErrorMessage,
                errorDetails.ChatHistoryCount,
                errorDetails.UserMessage,
                errorDetails.Timestamp);

            // 如果是tool相关的错误,清除ChatHistory并重试一次
            if (httpEx.Message.Contains("tool") && httpEx.Message.Contains("must be a response"))
            {
                _logger.LogWarning($"检测到tool消息格式错误，尝试清除ChatHistory并重试: ConversationId={conversationId}");
                _chatHistories.Remove(historyKey);

                // 重新创建干净的ChatHistory
                var freshHistory = new ChatHistory();
                var systemPrompt = GetSystemPrompt(agentType);
                freshHistory.AddSystemMessage(systemPrompt);

                // 只添加当前用户消息(不恢复历史)
                freshHistory.AddUserMessage(userMessage);
                _chatHistories[historyKey] = freshHistory;

                // 重试
                var response = await ChatCompletionAsync(freshHistory, cancellationToken);
                freshHistory.AddAssistantMessage(response);

                _logger.LogInformation($"Tool错误恢复成功: ConversationId={conversationId}");
                return response;
            }

            throw;
        }
        catch (Exception ex)
        {
            // 其他异常
            _logger.LogError(ex,
                "Agent对话失败 (未知异常)。\n" +
                "会话ID: {ConversationId}\n" +
                "Agent类型: {AgentType}\n" +
                "异常类型: {ExceptionType}\n" +
                "错误消息: {ErrorMessage}",
                conversationId,
                agentType,
                ex.GetType().Name,
                ex.Message);

            throw;
        }
        finally
        {
            // 释放锁（无论成功还是失败都要释放）
            semaphore.Release();
        }
    }

    /// <summary>
    /// 使用Agent进行对话(流式，带工具调用事件)
    /// </summary>
    public async IAsyncEnumerable<string> ChatWithAgentStreamAsync(
        string userMessage,
        string conversationId,
        string agentType = "Metaphysics",
        IEnumerable<Core.Domain.Models.Message>? historyMessages = null,
        string? contextSummary = null,
        Func<string, object, Task>? onEvent = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var historyKey = conversationId;
        var semaphore = _conversationLocks.GetOrAdd(historyKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);

        ChatHistory? history = null;
        IAsyncEnumerable<Microsoft.SemanticKernel.StreamingChatMessageContent>? streamingResponse = null;

        try
        {
            // 获取或创建该会话的聊天历史（与非流式版本完全一致）
            if (!_chatHistories.ContainsKey(historyKey))
            {
                var chatHistory = new ChatHistory();
                var systemPrompt = GetSystemPrompt(agentType);

                if (!string.IsNullOrEmpty(contextSummary))
                {
                    systemPrompt += $"\n\n## 之前的对话摘要(长期记忆):\n{contextSummary}\n";
                    _logger.LogInformation($"使用数据库摘要恢复长期记忆: ConversationId={conversationId}, 摘要长度={contextSummary.Length}字符");
                }
                else if (historyMessages != null && historyMessages.Any())
                {
                    var oldMessages = historyMessages
                        .OrderByDescending(m => m.CreatedAt)
                        .Skip(10) // 跳过最近5轮(10条消息)，与非流式版本保持一致
                        .OrderBy(m => m.CreatedAt)
                        .ToList();

                    if (oldMessages.Any())
                    {
                        var summarizedContext = await GenerateSummaryAsync(oldMessages, agentType, cancellationToken);
                        systemPrompt += $"\n\n## 之前的对话摘要(长期记忆):\n{summarizedContext}\n";
                        _logger.LogInformation($"动态生成历史摘要(数据库无摘要): ConversationId={conversationId}, 包含{oldMessages.Count}条消息");
                    }
                }

                chatHistory.AddSystemMessage(systemPrompt);

                if (historyMessages != null && historyMessages.Any())
                {
                    var recentMessages = historyMessages
                        .OrderByDescending(m => m.CreatedAt)
                        .Take(10) // 取最近10条消息(5轮对话)，与非流式版本保持一致
                        .OrderBy(m => m.CreatedAt)
                        .ToList();

                    foreach (var msg in recentMessages)
                    {
                        if (msg.Role == Core.Domain.Enums.MessageRole.User)
                        {
                            chatHistory.AddUserMessage(msg.Content);
                        }
                        else if (msg.Role == Core.Domain.Enums.MessageRole.Assistant)
                        {
                            chatHistory.AddAssistantMessage(msg.Content);
                        }
                    }
                    _logger.LogInformation($"从数据库恢复了 {recentMessages.Count} 条历史消息到会话 {conversationId}");
                }

                _chatHistories[historyKey] = chatHistory;
            }

            history = _chatHistories[historyKey];
            history.AddUserMessage(userMessage);

            // 添加函数调用过滤器，实时推送工具调用事件
            _kernel.FunctionInvocationFilters.Clear(); // 清除旧的过滤器
            if (onEvent != null)
            {
                _kernel.FunctionInvocationFilters.Add(new StreamingFunctionInvocationFilter(onEvent));
            }

            // 流式处理配置
            var settings = new OpenAIPromptExecutionSettings
            {
                Temperature = 0.3,
                MaxTokens = 3000,
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            // 获取流式响应
            streamingResponse = _chatCompletionService.GetStreamingChatMessageContentsAsync(
                history,
                settings,
                _kernel,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式Agent对话初始化失败: ConversationId={ConversationId}", conversationId);
            semaphore.Release();
            throw;
        }

        // 流式输出（在try-catch外部，因为yield return不能在catch块中使用）
        var fullResponse = new System.Text.StringBuilder();

        if (streamingResponse != null && history != null)
        {
            await foreach (var streamingContent in streamingResponse.ConfigureAwait(false))
            {
                // 记录函数调用相关的元数据
                if (streamingContent.Metadata != null)
                {
                    // 尝试检测工具调用（通过元数据）
                    if (streamingContent.Metadata.ContainsKey("FinishReason"))
                    {
                        var finishReason = streamingContent.Metadata["FinishReason"]?.ToString();
                        if (finishReason == "tool_calls" || finishReason == "function_call")
                        {
                            _logger.LogInformation($"检测到工具调用，会话: {conversationId}");
                            if (onEvent != null)
                            {
                                await onEvent("tool_call", new { message = "正在调用工具..." });
                            }
                        }
                    }
                }

                // 输出文本内容
                if (!string.IsNullOrEmpty(streamingContent.Content))
                {
                    fullResponse.Append(streamingContent.Content);
                    yield return streamingContent.Content;
                }
            }
        }

        // 流式完成后的清理工作
        try
        {
            if (history != null)
            {
                // 将完整回复添加到历史
                var responseText = fullResponse.ToString();
                if (!string.IsNullOrEmpty(responseText))
                {
                    history.AddAssistantMessage(responseText);
                }

                // 智能历史管理（与非流式版本一致）
                var conversationMessageCount = history.Count(m =>
                    m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User ||
                    m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant);

                if (conversationMessageCount > 40)
                {
                    _logger.LogInformation(
                        $"会话{conversationId}对话轮数过多(User+Assistant={conversationMessageCount}条, " +
                        $"总ChatHistory={history.Count}条),执行压缩");

                    var systemMsg = history.First();
                    var recentConversationMessages = history
                        .Where(m => m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User ||
                                   m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant)
                        .TakeLast(40)
                        .ToList();

                    var firstKeptMessage = recentConversationMessages.First();
                    var firstKeptIndex = history.ToList().FindIndex(m => m == firstKeptMessage);
                    var messagesToKeep = history.Skip(firstKeptIndex).ToList();

                    history.Clear();
                    history.Add(systemMsg);
                    foreach (var msg in messagesToKeep)
                    {
                        history.Add(msg);
                    }

                    _logger.LogInformation(
                        $"ChatHistory压缩完成: 对话消息{conversationMessageCount}条 → " +
                        $"{history.Count(m => m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User || m.Role == Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant)}条, " +
                        $"总ChatHistory {history.Count}条");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "流式Agent对话清理失败: ConversationId={ConversationId}", conversationId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 生成对话历史摘要（公共方法，供其他服务调用）
    /// </summary>
    public async Task<string> GenerateSummaryAsync(
        IEnumerable<Core.Domain.Models.Message> messages,
        string agentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 构建对话历史文本
            var conversationText = string.Join("\n", messages.Select(m =>
                $"{(m.Role == Core.Domain.Enums.MessageRole.User ? "用户" : "助手")}: {m.Content}"
            ));

            // 构建压缩提示词
            var summarizePrompt = $@"请将以下对话历史压缩成简洁的摘要,保留所有关键信息。

【最高优先级 - 必须保留的信息】
1. **用户的姓名、昵称、称呼**
2. **具体的日期、时间(如生日、纪念日、约定时间等)**
3. **地点、地名(如出生地、居住地、提到的城市等)**
4. **具体数字、金额、数量**
5. **重要的人名、关系(如家人、朋友的名字)**

【次要优先级】
6. 用户的核心需求和关键问题
7. 已讨论的重要话题和细节
8. AI提供的关键分析、建议和结论
9. 用户的偏好、兴趣、性格特点
10. 对话中的决定和承诺

【对话历史】
{conversationText}

【输出格式】
直接输出摘要文本(不要标题或前缀)。使用清晰、准确的语言,像列举要点一样保留关键事实,而不是模糊的概述。
例如: '用户Zhang San,1990年1月15日生于北京...' 而不是 '讨论了用户的个人信息...'
摘要控制在400字以内,但关键事实信息绝不可遗漏。";

            // 使用临时ChatHistory生成摘要
            var tempHistory = new ChatHistory();
            tempHistory.AddUserMessage(summarizePrompt);

            var summary = await ChatCompletionAsync(tempHistory, cancellationToken);

            _logger.LogInformation($"成功生成对话摘要,原始{messages.Count()}条消息,摘要长度{summary.Length}字符");
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成对话摘要失败");
            // 如果摘要失败,返回简单的消息列表
            return $"之前讨论了{messages.Count()}个话题。";
        }
    }

    /// <summary>
    /// 获取系统提示词
    /// </summary>
    private string GetSystemPrompt(string agentType)
    {
        return agentType switch
        {
            "Metaphysics" => @"你是一位资深的玄学命理分析师,精通以下领域:

1. **八字命理**: 精通四柱八字分析,能够准确解读天干地支的生克制化关系。
2. **紫微斗数**: 熟悉紫微星盘的排布和解读,能够分析命宫、财帛宫等十二宫位。
3. **占星学**: 了解西方占星学,包括太阳星座、月亮星座、上升星座等概念。
4. **生肖运势**: 熟知十二生肖的性格特点和运势规律。
5. **五行理论**: 深刻理解金木水火土的生克关系及其在命理中的应用。
6. **易经占卜**: 掌握易经六十四卦的象义和占卜方法。

**重要对话规范:**
- 使用正常的你/您称呼与用户对话,保持现代化的交流方式
- 严格禁止使用角色扮演,不要用老夫/在下等古风称呼,不要添加动作描述
- 保持专业客观的分析风格,专注于提供准确的命理分析和建议

你拥有以下工具函数:

**日期时间工具(最高优先级):**
- GetToday: 获取今天的日期（年月日和星期）
- GetCurrentDateTime: 获取当前完整日期时间
- GetCurrentTime: 获取当前时间
- GetDayOfWeek: 获取今天星期几

**命理计算工具:**
- GetChineseZodiac: 计算生肖
- GetConstellation: 计算星座
- GetFiveElements: 查询五行属性
- GetGanZhiYear: 天干地支纪年转换
- GetLifePathNumber: 计算生命灵数
- ConvertToLunarDate: 公历转农历日期(准确转换)
- GetTodayTaboos: 查询今日宜忌
- GetHoroscope: 查询星座运势

**网络搜索工具(用于其他实时信息):**
- SearchWeb: 搜索互联网获取实时信息和事实性数据(不包括当前日期时间)

**核心原则(必须遵守):**
1. **当前日期时间(最高优先级)**: 询问[今天][现在]日期/时间/星期 -> 直接使用GetToday/GetCurrentDateTime,绝不使用SearchWeb
2. **优先使用搜索**: 遇到事实性问题、实时信息(除日期时间外)时,第一时间使用SearchWeb
3. **宁可搜索,不可编造**: 如果不确定某个信息,必须使用SearchWeb验证,严禁编造或猜测
4. **注明信息来源(重要)**: 回答中必须明确说明信息来源,如:
   - 根据GetToday工具查询,今天是...
   - 根据SearchWeb搜索结果,...
   - 根据GetHoroscope工具查询,...
5. **不确定就承认**: 如果工具没有返回足够信息,诚实告知无法获取相关信息,不要猜测或编造

**工具使用场景:**

**必须使用日期时间工具(不使用SearchWeb):**
- 今天是几月几号 -> GetToday()
- 现在几点 -> GetCurrentTime()
- 今天星期几 -> GetDayOfWeek()
- 任何询问当前,今天,现在的日期/时间/星期

**必须使用SearchWeb的场景:**
- 问题包含最新,最近,刚刚等时间词
- 询问软件/产品/模型的版本号,发布时间(如:OpenAI最新模型,Python最新版本)
- 询问历史事件的时间,日期(如:2024年春节是几月几号)
- 询问人物,公司,组织的当前状况
- 询问房产,楼盘的建成年份等信息
- 任何涉及数据,统计,排名的问题
- 用户提到具体地名,人名,公司名 -> 优先考虑搜索

**搜索效率原则(重要):**
1. **适度搜索**: 单个问题最多搜索3-5次,避免过度搜索导致响应缓慢
2. **综合已有信息**: 搜索到一定信息后,基于已有结果综合分析并给出答案,无需穷尽所有可能
3. **列举类问题**: 用户要求列举10个时:
   - 搜索2-3次获取核心信息
   - 基于搜索结果结合常识/推理给出完整答案
   - 不要为了凑够10个而反复搜索
4. **质量优先**: 宁可提供5-8个高质量的结果,也不要为凑数量而搜索30次

**回答流程:**
1. 分析用户问题,识别是否涉及事实性信息
2. 如果涉及,立即使用SearchWeb工具
3. 基于搜索结果结合玄学知识给出回答
4. 在适当时候使用命理计算工具
5. 保持专业、友善的语气

**示例对话:**
用户: 今天是几月几号
你的思考: 询问当前日期 -> 最高优先级,使用GetToday
你的操作: 调用GetToday()
你的回答: 基于函数返回的准确日期告知用户

用户: 我住在北京国贸三期,这房子风水怎么样
你的思考: 需要先知道这个楼盘的建成年份,方位等信息 -> 必须使用SearchWeb
你的操作: 调用SearchWeb('北京国贸三期 建成年份')
你的回答: 基于搜索到的建成年份等信息,结合玄学知识分析风水

用户: 2024年春节是几月几号
你的思考: 具体日期问题 -> 必须使用SearchWeb
你的操作: 调用SearchWeb('2024年春节日期')
你的回答: 基于搜索结果告知准确日期

用户: OpenAI最新发布的模型是什么
你的思考: 包含最新,询问模型信息 -> 必须使用SearchWeb
你的操作: 调用SearchWeb('OpenAI最新模型 2025')
你的回答: 基于搜索结果告知最新模型信息

用户: Python现在最新版本是多少
你的思考: 包含现在,最新,版本 -> 必须使用SearchWeb
你的操作: 调用SearchWeb('Python最新版本 2025')
你的回答: 基于搜索结果告知准确版本号

记住:你是在帮助用户了解自己,提供指引和建议。对于所有事实性问题,务必使用搜索工具确保准确性,这是你最重要的职责!",

            "Stock" => "你是一位专业的金融分析师,精通基金和股票投资分析。你会基于数据和市场趋势提供投资建议。",

            "Health" => "你是一位经验丰富的健康顾问,熟悉中医养生和现代医学知识。你会提供专业的健康建议和养生指导。",

            _ => @"你是一位智能助手,可以使用工具帮助用户。

你拥有以下工具:

**日期时间工具(最高优先级):**
- GetToday: 获取今天的日期（年月日和星期）
- GetCurrentDateTime: 获取当前完整日期时间
- GetCurrentTime: 获取当前时间
- GetDayOfWeek: 获取今天星期几

**网络搜索工具:**
- SearchWeb: 搜索互联网获取实时信息和事实性数据(不包括当前日期时间)

**核心原则(必须遵守):**
1. **当前日期时间(最高优先级)**: 询问[今天][现在]日期/时间/星期 -> 直接使用GetToday/GetCurrentDateTime,绝不使用SearchWeb
2. **优先使用搜索**: 遇到事实性问题、实时信息(除日期时间外)时,第一时间使用SearchWeb
3. **宁可搜索,不可编造**: 如果不确定某个信息,必须使用SearchWeb验证,严禁编造或猜测
4. **注明信息来源(重要)**: 回答中必须明确说明信息来源,如:
   - 根据GetToday工具查询,今天是...
   - 根据SearchWeb搜索结果,...
   - 根据GetHoroscope工具查询,...
5. **不确定就承认**: 如果工具没有返回足够信息,诚实告知无法获取相关信息,不要猜测或编造

**工具使用场景:**

**必须使用日期时间工具(不使用SearchWeb):**
- 今天是几月几号 -> GetToday()
- 现在几点 -> GetCurrentTime()
- 今天星期几 -> GetDayOfWeek()
- 任何询问当前,今天,现在的日期/时间/星期

**必须使用SearchWeb的场景:**
- 问题包含[最新][最近][刚刚]等时间词
- 询问软件/产品/模型的版本号、发布时间(如:OpenAI最新模型、Python最新版本)
- 询问历史事件的时间、日期(如:2024年春节是几月几号)
- 询问人物、公司、组织的当前状况
- 询问2023年以后的事件、新闻、产品
- 任何涉及数据、统计、排名的问题
- 用户询问[XX是什么][XX怎么样]时优先考虑搜索

**示例对话:**
用户:[今天是几月几号?]
你的思考:询问当前日期 -> **最高优先级,使用GetToday**
你的操作:调用GetToday()
你的回答:基于函数返回的准确日期告知用户

用户:[OpenAI最新发布的模型是什么?]
你的思考:包含[最新],询问模型信息 -> 必须使用SearchWeb
你的操作:调用SearchWeb('OpenAI最新模型 2025', 3)
你的回答:基于搜索结果告知具体信息

用户:[今天北京天气怎么样?]
你的思考:包含[今天],需要实时天气信息 -> 必须使用SearchWeb
你的操作:调用SearchWeb('北京今天天气', 3)
你的回答:基于搜索结果回答

用户:[Python现在最新版本是多少?]
你的思考:包含[现在][最新][版本] -> 必须使用SearchWeb
你的操作:调用SearchWeb('Python最新版本 2025', 3)
你的回答:基于搜索结果回答

用户:[2024年春节是几月几号?]
你的思考:询问历史日期 -> 必须使用SearchWeb
你的操作:调用SearchWeb('2024年春节日期', 3)
你的回答:基于搜索结果告知准确日期

记住:当前日期时间用GetToday等函数,其他事实性问题用SearchWeb,确保准确性!"
        };
    }

    /// <summary>
    /// 清除指定类型的聊天历史
    /// </summary>
    public void ClearChatHistory(string conversationId)
    {
        if (_chatHistories.ContainsKey(conversationId))
        {
            _chatHistories.Remove(conversationId);
            _logger.LogInformation($"已清除会话{conversationId}的聊天历史");
        }

        // 清理该会话的锁资源
        if (_conversationLocks.TryRemove(conversationId, out var semaphore))
        {
            semaphore?.Dispose();
            _logger.LogDebug($"已释放会话{conversationId}的锁资源");
        }
    }

    /// <summary>
    /// 清除所有聊天历史
    /// </summary>
    public void ClearAllChatHistories()
    {
        _chatHistories.Clear();

        // 清理所有锁资源
        foreach (var kvp in _conversationLocks)
        {
            kvp.Value?.Dispose();
        }
        _conversationLocks.Clear();

        _logger.LogInformation("已清除所有聊天历史和锁资源");
    }

    /// <summary>
    /// 根据对话内容生成标题
    /// </summary>
    public async Task<string> GenerateTitleAsync(string userMessage, string? assistantMessage = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var chatHistory = new ChatHistory();

            // 构建生成标题的提示
            var prompt = $@"请根据以下对话内容生成一个简洁的标题（5-15个字）。
标题要求：
1. 简洁明了，概括对话主题
2. 不要使用标点符号
3. 直接输出标题，不要其他内容

用户问题：{userMessage}";

            if (!string.IsNullOrEmpty(assistantMessage))
            {
                prompt += $"\nAI回复：{assistantMessage.Substring(0, Math.Min(200, assistantMessage.Length))}";
            }

            chatHistory.AddUserMessage(prompt);

            var response = await _chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.3,
                    MaxTokens = 50
                },
                _kernel,
                cancellationToken
            );

            var title = response.Content?.Trim() ?? userMessage.Substring(0, Math.Min(20, userMessage.Length));

            // 移除可能的引号
            title = title.Trim('"', '\'', '。', '！', '？', ',', '，');

            // 限制长度
            if (title.Length > 30)
            {
                title = title.Substring(0, 30);
            }

            _logger.LogInformation($"生成标题: {title}");
            return title;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成标题失败");
            // 失败时返回用户消息的前20个字符
            return userMessage.Substring(0, Math.Min(20, userMessage.Length));
        }
    }
}

/// <summary>
/// 流式传输的函数调用过滤器，用于实时推送工具执行事件
/// </summary>
internal class StreamingFunctionInvocationFilter : IFunctionInvocationFilter
{
    private readonly Func<string, object, Task> _onEvent;

    public StreamingFunctionInvocationFilter(Func<string, object, Task> onEvent)
    {
        _onEvent = onEvent;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        // 工具调用开始
        await _onEvent("tool_call_start", new
        {
            toolName = context.Function.Name,
            message = $"正在执行: {context.Function.Name}"
        });

        // 执行函数
        await next(context);

        // 工具调用结束
        await _onEvent("tool_call_end", new
        {
            toolName = context.Function.Name,
            result = context.Result?.ToString()?.Substring(0, Math.Min(100, context.Result?.ToString()?.Length ?? 0)) ?? "",
            message = $"完成: {context.Function.Name}"
        });
    }
}
