namespace AgentHub.Contracts.DTOs.Conversation;

/// <summary>
/// 会话输出
/// </summary>
public class ConversationOutput
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 会话标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Agent类型
    /// </summary>
    public string AgentType { get; set; } = string.Empty;

    /// <summary>
    /// 会话状态
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 消息数量
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
