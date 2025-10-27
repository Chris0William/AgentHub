using System.ComponentModel.DataAnnotations;

namespace AgentHub.Contracts.DTOs.Conversation;

/// <summary>
/// 分页查询会话输入
/// </summary>
public class QueryPageConversationInput
{
    /// <summary>
    /// 页码（从1开始）
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "页码必须大于0")]
    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// 每页大小
    /// </summary>
    [Range(1, 100, ErrorMessage = "每页大小必须在1-100之间")]
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Agent类型筛选（可选）
    /// </summary>
    public string? AgentType { get; set; }
}
