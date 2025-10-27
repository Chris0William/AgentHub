namespace AgentHub.Contracts.DTOs.Chat;

/// <summary>
/// 聊天消息响应
/// </summary>
public class ChatMessageResponse
{
    /// <summary>
    /// 用户消息ID
    /// </summary>
    public long UserMessageId { get; set; }

    /// <summary>
    /// AI回复消息ID
    /// </summary>
    public long AssistantMessageId { get; set; }

    /// <summary>
    /// 用户消息内容
    /// </summary>
    public string UserMessage { get; set; } = string.Empty;

    /// <summary>
    /// AI回复内容
    /// </summary>
    public string AssistantMessage { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
