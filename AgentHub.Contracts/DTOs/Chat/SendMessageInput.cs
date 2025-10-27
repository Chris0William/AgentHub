using System.ComponentModel.DataAnnotations;

namespace AgentHub.Contracts.DTOs.Chat;

/// <summary>
/// 发送消息输入
/// </summary>
public class SendMessageInput
{
    /// <summary>
    /// 会话ID
    /// </summary>
    [Required(ErrorMessage = "会话ID不能为空")]
    public long ConversationId { get; set; }

    /// <summary>
    /// 消息内容
    /// </summary>
    [Required(ErrorMessage = "消息内容不能为空")]
    [StringLength(4000, ErrorMessage = "消息内容不能超过4000字符")]
    public string Content { get; set; } = string.Empty;
}
