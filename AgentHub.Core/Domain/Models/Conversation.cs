using AgentHub.Core.Domain.Enums;

namespace AgentHub.Core.Domain.Models;

/// <summary>
/// 会话实体
/// </summary>
public class Conversation
{
    /// <summary>
    /// 会话ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Agent类型
    /// </summary>
    public AgentType AgentType { get; set; }

    /// <summary>
    /// 会话标题
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// 会话状态
    /// </summary>
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 最后消息时间
    /// </summary>
    public DateTime? LastMessageAt { get; set; }

    /// <summary>
    /// 上下文摘要(压缩后)
    /// </summary>
    public string? ContextSummary { get; set; }

    /// <summary>
    /// 元数据(JSON)
    /// </summary>
    public string? MetadataJson { get; set; }

    // 导航属性
    /// <summary>
    /// 关联的用户
    /// </summary>
    public User User { get; set; } = null!;

    /// <summary>
    /// 消息列表
    /// </summary>
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
