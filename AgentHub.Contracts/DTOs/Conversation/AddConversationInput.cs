using System.ComponentModel.DataAnnotations;

namespace AgentHub.Contracts.DTOs.Conversation;

/// <summary>
/// 创建会话输入
/// </summary>
public class AddConversationInput
{
    /// <summary>
    /// Agent类型 (Metaphysics/Stock/Health)
    /// </summary>
    [Required(ErrorMessage = "Agent类型不能为空")]
    public string AgentType { get; set; } = "Metaphysics";

    /// <summary>
    /// 会话标题（可选，系统会自动生成）
    /// </summary>
    [StringLength(200, ErrorMessage = "标题长度不能超过200字符")]
    public string? Title { get; set; }
}
