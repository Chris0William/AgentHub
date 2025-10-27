using AgentHub.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Collections.Concurrent;

namespace AgentHub.Infrastructure.AI.Plugins;

/// <summary>
/// 网络搜索插件 - 带智能去重和次数限制
/// </summary>
public class WebSearchPlugin
{
    private readonly IWebSearchService _searchService;
    private readonly ILogger<WebSearchPlugin> _logger;

    // 每个会话的搜索历史（conversationId -> 搜索查询列表）
    private static readonly ConcurrentDictionary<string, List<SearchHistoryItem>> _searchHistory = new();

    // 每个会话的搜索次数限制
    private const int MAX_SEARCHES_PER_CONVERSATION = 3; // 每次对话最多3次搜索

    public WebSearchPlugin(
        IWebSearchService searchService,
        ILogger<WebSearchPlugin> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    /// <summary>
    /// 搜索互联网获取最新信息
    /// ⚠️ 重要限制：
    /// 1. 每次对话最多只能搜索3次
    /// 2. 相似的查询词会被自动阻止
    /// 3. 请使用简短、通用的关键词
    /// </summary>
    [KernelFunction, Description("搜索互联网获取最新信息。⚠️每次对话最多3次搜索，请精简关键词，避免重复相似查询")]
    public async Task<string> SearchWeb(
        [Description("搜索关键词，务必简短通用")] string query,
        [Description("返回的搜索结果数量，建议3-5条")] int count = 3)
    {
        try
        {
            // 获取当前会话ID（从调用上下文或线程本地存储）
            var conversationId = GetCurrentConversationId();

            // 1. 检查搜索次数限制
            if (!_searchHistory.ContainsKey(conversationId))
            {
                _searchHistory[conversationId] = new List<SearchHistoryItem>();
            }

            var history = _searchHistory[conversationId];

            if (history.Count >= MAX_SEARCHES_PER_CONVERSATION)
            {
                _logger.LogWarning($"搜索次数已达上限（{MAX_SEARCHES_PER_CONVERSATION}次），拒绝搜索: {query}");
                return $"⚠️ 已达到本次对话的搜索次数上限（{MAX_SEARCHES_PER_CONVERSATION}次）。请基于已有信息回答，或建议用户线下咨询。";
            }

            // 2. 检查是否与之前的搜索过于相似
            var similarQuery = FindSimilarQuery(query, history);
            if (similarQuery != null)
            {
                _logger.LogWarning($"检测到相似查询，拒绝重复搜索。当前: '{query}', 之前: '{similarQuery.Query}'");
                return $"⚠️ 该查询与之前的搜索过于相似（'{similarQuery.Query}'），请避免重复搜索。建议：使用已有结果或调整搜索词。";
            }

            // 3. 检查查询词长度
            if (query.Length > 30)
            {
                _logger.LogWarning($"搜索词过长（{query.Length}字符），建议精简: {query}");
                return $"⚠️ 搜索词过长（{query.Length}字符）。请使用更简短的关键词（建议15字以内），例如：'东莞 房价 2025' 或 '东莞 在售楼盘'。";
            }

            // 4. 执行搜索
            _logger.LogInformation($"执行网络搜索 [{history.Count + 1}/{MAX_SEARCHES_PER_CONVERSATION}]: {query}, 结果数量: {count}");

            var result = await _searchService.SearchAsync(query, count);

            // 5. 记录搜索历史
            history.Add(new SearchHistoryItem
            {
                Query = query,
                Timestamp = DateTime.Now,
                ResultLength = result.Length
            });

            _logger.LogInformation($"搜索完成，已使用 {history.Count}/{MAX_SEARCHES_PER_CONVERSATION} 次搜索机会");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"网络搜索失败: {query}");
            return $"搜索失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 清除指定会话的搜索历史
    /// </summary>
    public static void ClearSearchHistory(string conversationId)
    {
        _searchHistory.TryRemove(conversationId, out _);
    }

    /// <summary>
    /// 获取当前会话ID（简化版，实际应从上下文获取）
    /// </summary>
    private string GetCurrentConversationId()
    {
        // TODO: 从Semantic Kernel上下文或线程本地存储获取真实的conversationId
        // 暂时使用线程ID作为会话标识
        return $"thread_{Environment.CurrentManagedThreadId}";
    }

    /// <summary>
    /// 查找相似的历史查询
    /// 使用简单的文本相似度算法
    /// </summary>
    private SearchHistoryItem? FindSimilarQuery(string newQuery, List<SearchHistoryItem> history)
    {
        var newQueryNormalized = NormalizeQuery(newQuery);

        foreach (var item in history)
        {
            var historyQueryNormalized = NormalizeQuery(item.Query);

            // 计算相似度（简单版：检查包含关系和编辑距离）
            var similarity = CalculateSimilarity(newQueryNormalized, historyQueryNormalized);

            if (similarity > 0.7) // 相似度超过70%认为是重复
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// 标准化查询词（去除常见的无意义词汇）
    /// </summary>
    private string NormalizeQuery(string query)
    {
        // 转小写
        var normalized = query.ToLower();

        // 移除常见的无意义词
        var stopWords = new[] { "的", "了", "吗", "呢", "啊", "吧", "嘛",
            "最新", "详细", "具体", "完整", "全部", "所有", "查询", "信息", "数据", "资料",
            "列表", "名单", "项目", "推荐", "有哪些", "怎么样", "如何" };

        foreach (var stopWord in stopWords)
        {
            normalized = normalized.Replace(stopWord, " ");
        }

        // 移除多余空格
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized.Trim();
    }

    /// <summary>
    /// 计算两个查询词的相似度（0-1之间）
    /// </summary>
    private double CalculateSimilarity(string query1, string query2)
    {
        if (query1 == query2) return 1.0;

        // 分词（简单按空格分）
        var words1 = query1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words2 = query2.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 计算交集和并集
        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        if (union == 0) return 0.0;

        // Jaccard相似度
        return (double)intersection / union;
    }
}

/// <summary>
/// 搜索历史记录项
/// </summary>
public class SearchHistoryItem
{
    public string Query { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public int ResultLength { get; set; }
}
