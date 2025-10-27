using AgentHub.Core.Domain.Enums;

namespace AgentHub.Core.Domain.Models;

/// <summary>
/// 消息实体
/// </summary>
public class Message
{
    /// <summary>
    /// 消息ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 会话ID
    /// </summary>
    public long ConversationId { get; set; }

    /// <summary>
    /// 角色
    /// </summary>
    public MessageRole Role { get; set; }

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 元数据(JSON) - 包含工具调用、图片等信息
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// 消耗Token数
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// 模型版本
    /// </summary>
    public string? ModelVersion { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 导航属性
    /// <summary>
    /// 关联的会话
    /// </summary>
    public Conversation Conversation { get; set; } = null!;
}
