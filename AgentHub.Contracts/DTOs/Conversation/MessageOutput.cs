namespace AgentHub.Contracts.DTOs.Conversation;

/// <summary>
/// 消息输出
/// </summary>
public class MessageOutput
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 角色 (user/assistant/system)
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 工具调用历史记录
    /// </summary>
    public List<ToolCallHistoryItemOutput>? ToolCallHistory { get; set; }
}

/// <summary>
/// 工具调用历史项输出
/// </summary>
public class ToolCallHistoryItemOutput
{
    /// <summary>
    /// 调用时间戳
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 状态消息
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
