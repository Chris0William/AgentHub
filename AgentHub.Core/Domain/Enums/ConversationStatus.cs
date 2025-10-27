namespace AgentHub.Core.Domain.Enums;

/// <summary>
/// 会话状态枚举
/// </summary>
public enum ConversationStatus
{
    /// <summary>
    /// 活跃中
    /// </summary>
    Active,

    /// <summary>
    /// 已完成
    /// </summary>
    Completed,

    /// <summary>
    /// 已放弃
    /// </summary>
    Abandoned
}
