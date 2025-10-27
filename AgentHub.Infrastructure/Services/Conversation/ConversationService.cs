using AgentHub.Core.Domain.Enums;
using AgentHub.Core.Domain.Models;
using AgentHub.Core.Interfaces;
using AgentHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentHub.Infrastructure.Services.Conversation;

/// <summary>
/// 会话管理服务实现
/// </summary>
public class ConversationService : IConversationService
{
    private readonly AgentHubDbContext _context;
    private readonly ILogger<ConversationService> _logger;
    private readonly ISemanticKernelService _semanticKernelService;
    private readonly IServiceScopeFactory _scopeFactory;

    public ConversationService(
        AgentHubDbContext context,
        ILogger<ConversationService> logger,
        ISemanticKernelService semanticKernelService,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _logger = logger;
        _semanticKernelService = semanticKernelService;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// 创建新会话
    /// </summary>
    public async Task<Core.Domain.Models.Conversation> CreateConversationAsync(
        long userId,
        AgentType agentType,
        string? title = null)
    {
        try
        {
            var conversation = new Core.Domain.Models.Conversation
            {
                UserId = userId,
                Title = title ?? GenerateDefaultTitle(agentType),
                AgentType = agentType,
                Status = ConversationStatus.Active,
                StartedAt = DateTime.Now,
                LastMessageAt = null
            };

            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"创建会话成功: ConversationId={conversation.Id}, UserId={userId}, AgentType={agentType}");

            return conversation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"创建会话失败: UserId={userId}, AgentType={agentType}");
            throw;
        }
    }

    /// <summary>
    /// 获取用户的所有会话（分页）
    /// </summary>
    public async Task<(List<Core.Domain.Models.Conversation> conversations, int total)> GetUserConversationsAsync(
        long userId,
        int pageIndex = 1,
        int pageSize = 20,
        AgentType? agentType = null)
    {
        try
        {
            var query = _context.Conversations
                .Where(c => c.UserId == userId)
                .Include(c => c.Messages)
                .AsQueryable();

            // 筛选Agent类型
            if (agentType.HasValue)
            {
                query = query.Where(c => c.AgentType == agentType.Value);
            }

            var total = await query.CountAsync();

            var conversations = await query
                .OrderByDescending(c => c.LastMessageAt ?? c.StartedAt)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (conversations, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取用户会话列表失败: UserId={userId}");
            throw;
        }
    }

    /// <summary>
    /// 获取会话详情（含消息）
    /// </summary>
    public async Task<Core.Domain.Models.Conversation?> GetConversationWithMessagesAsync(long conversationId, long userId)
    {
        try
        {
            var conversation = await _context.Conversations
                .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

            return conversation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取会话详情失败: ConversationId={conversationId}, UserId={userId}");
            throw;
        }
    }

    /// <summary>
    /// 添加消息到会话
    /// </summary>
    public async Task<Message> AddMessageAsync(long conversationId, MessageRole role, string content, long userId, string? metadataJson = null)
    {
        try
        {
            // 验证会话归属
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

            if (conversation == null)
            {
                throw new InvalidOperationException($"会话不存在或无权访问: ConversationId={conversationId}");
            }

            var message = new Message
            {
                ConversationId = conversationId,
                Role = role,
                Content = content,
                MetadataJson = metadataJson,
                CreatedAt = DateTime.Now
            };

            _context.Messages.Add(message);

            // 更新会话的最后消息时间
            conversation.LastMessageAt = DateTime.Now;

            // 如果是第一条用户消息,自动生成标题
            var messageCount = await _context.Messages
                .CountAsync(m => m.ConversationId == conversationId);

            if (messageCount == 0 && role == MessageRole.User && (conversation.Title?.StartsWith("新对话") == true || conversation.Title?.Contains("咨询") == true))
            {
                conversation.Title = GenerateTitleFromMessage(content, conversation.AgentType);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation($"添加消息成功: MessageId={message.Id}, ConversationId={conversationId}");

            // 自动触发摘要更新（异步后台执行，不阻塞响应）
            // 每10条消息（约5轮对话）更新一次摘要
            // 注意: messageCount是添加当前消息前的计数,所以需要+1
            var currentMessageCount = messageCount + 1;
            if (currentMessageCount >= 12 && currentMessageCount % 10 == 2)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 创建新的 scope，避免使用已 dispose 的 DbContext
                        using var scope = _scopeFactory.CreateScope();
                        var scopedConversationService = scope.ServiceProvider.GetRequiredService<IConversationService>();

                        // 使用新 scope 中的服务执行更新
                        await ((ConversationService)scopedConversationService).UpdateContextSummaryAsync(conversationId, userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"后台更新摘要失败: ConversationId={conversationId}");
                    }
                });
            }

            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"添加消息失败: ConversationId={conversationId}");
            throw;
        }
    }

    /// <summary>
    /// 更新会话标题
    /// </summary>
    public async Task<bool> UpdateConversationTitleAsync(long conversationId, string title, long userId)
    {
        try
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

            if (conversation == null)
            {
                return false;
            }

            conversation.Title = title;
            // 不需要更新时间，标题更新不影响LastMessageAt

            await _context.SaveChangesAsync();

            _logger.LogInformation($"更新会话标题成功: ConversationId={conversationId}");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"更新会话标题失败: ConversationId={conversationId}");
            throw;
        }
    }

    /// <summary>
    /// 删除会话
    /// </summary>
    public async Task<bool> DeleteConversationAsync(long conversationId, long userId)
    {
        try
        {
            var conversation = await _context.Conversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

            if (conversation == null)
            {
                return false;
            }

            // 删除所有消息
            _context.Messages.RemoveRange(conversation.Messages);

            // 删除会话
            _context.Conversations.Remove(conversation);

            await _context.SaveChangesAsync();

            _logger.LogInformation($"删除会话成功: ConversationId={conversationId}");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除会话失败: ConversationId={conversationId}");
            throw;
        }
    }

    /// <summary>
    /// 获取会话消息列表
    /// </summary>
    public async Task<List<Message>> GetConversationMessagesAsync(long conversationId, long userId, int limit = 50)
    {
        try
        {
            // 验证会话归属
            var conversationExists = await _context.Conversations
                .AnyAsync(c => c.Id == conversationId && c.UserId == userId);

            if (!conversationExists)
            {
                throw new InvalidOperationException($"会话不存在或无权访问: ConversationId={conversationId}");
            }

            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取会话消息失败: ConversationId={conversationId}");
            throw;
        }
    }

    /// <summary>
    /// 生成默认标题
    /// </summary>
    private string GenerateDefaultTitle(AgentType agentType)
    {
        var prefix = agentType switch
        {
            AgentType.Metaphysics => "玄学咨询",
            AgentType.Stock => "投资咨询",
            AgentType.Health => "健康咨询",
            _ => "新对话"
        };

        return $"{prefix} - {DateTime.Now:MM-dd HH:mm}";
    }

    /// <summary>
    /// 根据第一条消息生成标题
    /// </summary>
    private string GenerateTitleFromMessage(string content, AgentType agentType)
    {
        // 取前20个字符作为标题
        var title = content.Length > 20 ? content.Substring(0, 20) + "..." : content;

        var prefix = agentType switch
        {
            AgentType.Metaphysics => "玄学",
            AgentType.Stock => "投资",
            AgentType.Health => "健康",
            _ => "对话"
        };

        return $"{prefix}:{title}";
    }

    /// <summary>
    /// 更新会话的上下文摘要（持久化到数据库）
    /// </summary>
    /// <param name="conversationId">会话ID</param>
    /// <param name="userId">用户ID</param>
    private async Task UpdateContextSummaryAsync(long conversationId, long userId)
    {
        try
        {
            // 获取会话及其所有消息
            var conversation = await _context.Conversations
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId);

            if (conversation == null)
            {
                _logger.LogWarning($"更新摘要失败：会话不存在或无权访问: ConversationId={conversationId}");
                return;
            }

            // 获取所有需要摘要的消息（除最近6条消息，即最近3轮对话）
            var allMessages = conversation.Messages
                .OrderBy(m => m.CreatedAt)
                .ToList();

            // 至少需要12条消息（6轮对话）才生成摘要
            if (allMessages.Count < 12)
            {
                _logger.LogInformation($"会话消息数不足12条，跳过摘要生成: ConversationId={conversationId}");
                return;
            }

            // 跳过最近6条消息
            var oldMessages = allMessages
                .Take(allMessages.Count - 6)
                .ToList();

            if (oldMessages.Count == 0)
            {
                _logger.LogInformation($"没有需要摘要的历史消息: ConversationId={conversationId}");
                return;
            }

            // 调用 Semantic Kernel 生成摘要
            _logger.LogInformation($"开始生成摘要: ConversationId={conversationId}, 消息数={oldMessages.Count}");

            var summary = await _semanticKernelService.GenerateSummaryAsync(
                oldMessages,
                conversation.AgentType.ToString(),
                CancellationToken.None);

            // 持久化到数据库
            conversation.ContextSummary = summary;
            await _context.SaveChangesAsync();

            // 清除内存中的ChatHistory缓存,强制下次请求重新创建并加载新摘要
            _semanticKernelService.ClearChatHistory(conversationId.ToString());
            _logger.LogInformation($"已清除会话{conversationId}的ChatHistory缓存,下次请求将加载新摘要");

            _logger.LogInformation(
                $"成功更新会话摘要: ConversationId={conversationId}, " +
                $"摘要长度={summary.Length}字符, " +
                $"覆盖消息数={oldMessages.Count}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"更新会话摘要失败: ConversationId={conversationId}");
            // 不抛出异常，避免影响主流程
        }
    }
}
