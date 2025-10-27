namespace AgentHub.Core.Interfaces;

/// <summary>
/// LLM服务接口
/// </summary>
public interface ILLMService
{
    /// <summary>
    /// 发送聊天消息(非流式)
    /// </summary>
    Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送聊天消息(流式)
    /// </summary>
    IAsyncEnumerable<string> ChatStreamAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>
/// 聊天消息
/// </summary>
public class ChatMessage
{
    /// <summary>
    /// 角色
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 内容
    /// </summary>
    public string Content { get; set; } = string.Empty;
}
