namespace AgentHub.Contracts.DTOs.Chat;

/// <summary>
/// 聊天响应
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// AI回复内容
    /// </summary>
    public string Reply { get; set; } = string.Empty;

    /// <summary>
    /// 会话ID
    /// </summary>
    public long ConversationId { get; set; }

    /// <summary>
    /// 消息ID
    /// </summary>
    public long MessageId { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
