namespace AgentHub.Core.Domain.Models;

/// <summary>
/// 消息元数据
/// </summary>
public class MessageMetadata
{
    /// <summary>
    /// 工具调用历史记录
    /// </summary>
    public List<ToolCallHistoryItem>? ToolCallHistory { get; set; }
}

/// <summary>
/// 工具调用历史项
/// </summary>
public class ToolCallHistoryItem
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
