using System.ComponentModel.DataAnnotations;

namespace AgentHub.Contracts.DTOs.Chat;

/// <summary>
/// 聊天请求
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// 用户消息
    /// </summary>
    [Required(ErrorMessage = "消息内容不能为空")]
    [StringLength(4000, ErrorMessage = "消息内容不能超过4000字符")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 会话ID(可选,用于继续之前的对话)
    /// </summary>
    public long? ConversationId { get; set; }

    /// <summary>
    /// Agent类型(可选,默认为Metaphysics)
    /// </summary>
    public string? AgentType { get; set; }

    /// <summary>
    /// 是否使用流式输出
    /// </summary>
    public bool Stream { get; set; } = false;
}
