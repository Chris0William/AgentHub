using AgentHub.Core.Domain.Models;
using AgentHub.Core.Domain.Enums;

namespace AgentHub.Core.Interfaces;

/// <summary>
/// 会话管理服务接口
/// </summary>
public interface IConversationService
{
    /// <summary>
    /// 创建新会话
    /// </summary>
    Task<Conversation> CreateConversationAsync(long userId, AgentType agentType, string? title = null);

    /// <summary>
    /// 获取用户的所有会话（分页）
    /// </summary>
    Task<(List<Conversation> conversations, int total)> GetUserConversationsAsync(
        long userId,
        int pageIndex = 1,
        int pageSize = 20,
        AgentType? agentType = null);

    /// <summary>
    /// 获取会话详情（含消息）
    /// </summary>
    Task<Conversation?> GetConversationWithMessagesAsync(long conversationId, long userId);

    /// <summary>
    /// 添加消息到会话
    /// </summary>
    Task<Message> AddMessageAsync(long conversationId, MessageRole role, string content, long userId, string? metadataJson = null);

    /// <summary>
    /// 更新会话标题
    /// </summary>
    Task<bool> UpdateConversationTitleAsync(long conversationId, string title, long userId);

    /// <summary>
    /// 删除会话
    /// </summary>
    Task<bool> DeleteConversationAsync(long conversationId, long userId);

    /// <summary>
    /// 获取会话消息列表
    /// </summary>
    Task<List<Message>> GetConversationMessagesAsync(long conversationId, long userId, int limit = 50);
}
