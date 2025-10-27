namespace AgentHub.Contracts.DTOs.Conversation;

/// <summary>
/// 会话详情输出（含消息列表）
/// </summary>
public class ConversationDetailOutput : ConversationOutput
{
    /// <summary>
    /// 消息列表
    /// </summary>
    public List<MessageOutput> Messages { get; set; } = new();
}
