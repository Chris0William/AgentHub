using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentHub.Core.Interfaces;

/// <summary>
/// Semantic Kernel服务接口
/// </summary>
public interface ISemanticKernelService
{
    /// <summary>
    /// 获取Kernel实例
    /// </summary>
    Kernel GetKernel();

    /// <summary>
    /// 聊天补全(非流式)
    /// </summary>
    Task<string> ChatCompletionAsync(ChatHistory chatHistory, CancellationToken cancellationToken = default);

    /// <summary>
    /// 聊天补全(流式)
    /// </summary>
    IAsyncEnumerable<string> ChatCompletionStreamAsync(ChatHistory chatHistory, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用Agent进行对话
    /// </summary>
    /// <param name="userMessage">用户消息</param>
    /// <param name="conversationId">会话ID</param>
    /// <param name="agentType">Agent类型</param>
    /// <param name="historyMessages">历史消息(用于恢复上下文)</param>
    /// <param name="contextSummary">历史对话摘要(长期记忆)</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<string> ChatWithAgentAsync(string userMessage, string conversationId, string agentType = "Metaphysics", IEnumerable<Domain.Models.Message>? historyMessages = null, string? contextSummary = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用Agent进行对话(流式，带工具调用事件)
    /// </summary>
    /// <param name="userMessage">用户消息</param>
    /// <param name="conversationId">会话ID</param>
    /// <param name="agentType">Agent类型</param>
    /// <param name="historyMessages">历史消息(用于恢复上下文)</param>
    /// <param name="contextSummary">历史对话摘要(长期记忆)</param>
    /// <param name="onEvent">事件回调(eventType, data)</param>
    /// <param name="cancellationToken">取消令牌</param>
    IAsyncEnumerable<string> ChatWithAgentStreamAsync(string userMessage, string conversationId, string agentType = "Metaphysics", IEnumerable<Domain.Models.Message>? historyMessages = null, string? contextSummary = null, Func<string, object, Task>? onEvent = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除指定会话的聊天历史
    /// </summary>
    void ClearChatHistory(string conversationId);

    /// <summary>
    /// 清除所有聊天历史
    /// </summary>
    void ClearAllChatHistories();

    /// <summary>
    /// 生成对话历史摘要
    /// </summary>
    /// <param name="messages">需要压缩的消息列表</param>
    /// <param name="agentType">Agent类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩后的摘要文本</returns>
    Task<string> GenerateSummaryAsync(IEnumerable<Domain.Models.Message> messages, string agentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据对话内容生成标题
    /// </summary>
    /// <param name="userMessage">用户消息</param>
    /// <param name="assistantMessage">AI回复</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>生成的标题</returns>
    Task<string> GenerateTitleAsync(string userMessage, string? assistantMessage = null, CancellationToken cancellationToken = default);
}
