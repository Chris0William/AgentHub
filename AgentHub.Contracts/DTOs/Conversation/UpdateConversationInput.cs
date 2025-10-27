using System.ComponentModel.DataAnnotations;

namespace AgentHub.Contracts.DTOs.Conversation;

/// <summary>
/// 更新会话输入
/// </summary>
public class UpdateConversationInput
{
    /// <summary>
    /// 会话ID
    /// </summary>
    [Required(ErrorMessage = "会话ID不能为空")]
    public long ConversationId { get; set; }

    /// <summary>
    /// 新标题
    /// </summary>
    [Required(ErrorMessage = "标题不能为空")]
    [StringLength(200, ErrorMessage = "标题长度不能超过200字符")]
    public string Title { get; set; } = string.Empty;
}
