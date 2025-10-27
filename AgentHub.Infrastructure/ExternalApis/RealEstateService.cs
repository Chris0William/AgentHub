using AgentHub.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace AgentHub.Infrastructure.ExternalApis;

/// <summary>
/// 房产数据服务实现 - 支持多数据源
/// 当前支持：安居客爬虫 + SearXNG搜索
/// </summary>
public class RealEstateService : IRealEstateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RealEstateService> _logger;
    private readonly IWebSearchService _searchService;
    private readonly string _apiKey;

    // 数据缓存（city_keyword -> (result, timestamp)）
    private static readonly ConcurrentDictionary<string, (string Result, DateTime Timestamp)> _cache = new();
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(6); // 缓存6小时

    public RealEstateService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<RealEstateService> logger,
        IWebSearchService searchService)
    {
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
        _searchService = searchService;
        _apiKey = configuration["ExternalApis:RealEstate:ApiKey"] ?? "";

        // 配置HttpClient模拟真实浏览器
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/html");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");

        _logger.LogInformation("房产数据服务已初始化");
    }

    /// <summary>
    /// 搜索楼盘信息（通过SearXNG）
    /// </summary>
    public async Task<string> SearchPropertyAsync(
        string city,
        string? keyword = null,
        int? minPrice = null,
        int? maxPrice = null,
        int limit = 10)
    {
        try
        {
            // 检查缓存
            var cacheKey = $"{city}_{keyword}_{minPrice}_{maxPrice}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.Now - cached.Timestamp < _cacheExpiration)
                {
                    _logger.LogInformation($"从缓存返回楼盘数据: {cacheKey}");
                    return cached.Result;
                }
                else
                {
                    _cache.TryRemove(cacheKey, out _);
                }
            }

            // 构建搜索查询
            var searchQuery = BuildSearchQuery(city, keyword, minPrice, maxPrice);

            _logger.LogInformation($"搜索楼盘: {searchQuery}");

            // 使用SearXNG搜索
            var searchResult = await _searchService.SearchAsync(searchQuery, Math.Min(limit, 5));

            // 格式化结果
            var result = FormatPropertySearchResult(searchResult, city, minPrice, maxPrice);

            // 缓存结果
            _cache.TryAdd(cacheKey, (result, DateTime.Now));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"搜索楼盘失败: city={city}, keyword={keyword}");
            return $"搜索楼盘失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 获取房价趋势
    /// </summary>
    public async Task<string> GetPriceTrendAsync(string city, string? district = null)
    {
        try
        {
            var cacheKey = $"trend_{city}_{district}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.Now - cached.Timestamp < _cacheExpiration)
                {
                    return cached.Result;
                }
                _cache.TryRemove(cacheKey, out _);
            }

            // 使用搜索引擎获取房价趋势
            var searchQuery = district != null
                ? $"{city} {district} 房价走势 2025"
                : $"{city} 房价走势 2025";

            _logger.LogInformation($"查询房价趋势: {searchQuery}");

            var searchResult = await _searchService.SearchAsync(searchQuery, 3);

            var result = $"**{city}{(district != null ? district : "")}房价趋势**\n\n{searchResult}\n\n" +
                         $"💡 提示：以上数据来自网络搜索，实际房价以楼盘最新报价为准。";

            _cache.TryAdd(cacheKey, (result, DateTime.Now));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取房价趋势失败: city={city}, district={district}");
            return $"获取房价趋势失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 获取楼盘详情
    /// </summary>
    public async Task<string> GetPropertyDetailAsync(string propertyId)
    {
        try
        {
            var cacheKey = $"detail_{propertyId}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.Now - cached.Timestamp < _cacheExpiration)
                {
                    return cached.Result;
                }
                _cache.TryRemove(cacheKey, out _);
            }

            // 搜索楼盘详情
            var searchQuery = $"{propertyId} 楼盘详情 均价 户型 地址";
            _logger.LogInformation($"查询楼盘详情: {searchQuery}");

            var searchResult = await _searchService.SearchAsync(searchQuery, 5);

            var result = $"**{propertyId} 楼盘详情**\n\n{searchResult}";

            _cache.TryAdd(cacheKey, (result, DateTime.Now));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"获取楼盘详情失败: propertyId={propertyId}");
            return $"获取楼盘详情失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 推荐楼盘
    /// </summary>
    public async Task<string> RecommendPropertyAsync(
        string city,
        int budget,
        string? rooms = null,
        string? district = null)
    {
        try
        {
            var cacheKey = $"recommend_{city}_{budget}_{rooms}_{district}";
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.Now - cached.Timestamp < TimeSpan.FromHours(2)) // 推荐缓存2小时
                {
                    return cached.Result;
                }
                _cache.TryRemove(cacheKey, out _);
            }

            // 构建推荐搜索查询
            var searchQuery = BuildRecommendQuery(city, budget, rooms, district);

            _logger.LogInformation($"推荐楼盘: {searchQuery}");

            var searchResult = await _searchService.SearchAsync(searchQuery, 5);

            var result = $"**{city} 楼盘推荐**\n" +
                         $"预算：{budget}万元{(rooms != null ? $"，户型：{rooms}" : "")}{(district != null ? $"，区域：{district}" : "")}\n\n" +
                         $"{searchResult}\n\n" +
                         $"💡 建议：实地看房时可结合风水方位、楼层数字等玄学因素综合考虑。";

            _cache.TryAdd(cacheKey, (result, DateTime.Now));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"推荐楼盘失败: city={city}, budget={budget}");
            return $"推荐楼盘失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 构建搜索查询
    /// </summary>
    private string BuildSearchQuery(string city, string? keyword, int? minPrice, int? maxPrice)
    {
        var parts = new List<string> { city, "在售楼盘", "2025" };

        if (!string.IsNullOrEmpty(keyword))
        {
            parts.Add(keyword);
        }

        if (minPrice.HasValue && maxPrice.HasValue)
        {
            parts.Add($"{minPrice}-{maxPrice}万");
        }
        else if (minPrice.HasValue)
        {
            parts.Add($"{minPrice}万以上");
        }
        else if (maxPrice.HasValue)
        {
            parts.Add($"{maxPrice}万以下");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 构建推荐查询
    /// </summary>
    private string BuildRecommendQuery(string city, int budget, string? rooms, string? district)
    {
        var parts = new List<string> { city };

        if (!string.IsNullOrEmpty(district))
        {
            parts.Add(district);
        }

        parts.Add("在售楼盘");
        parts.Add($"{budget}万左右");

        if (!string.IsNullOrEmpty(rooms))
        {
            parts.Add(rooms);
        }

        parts.Add("推荐");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 格式化搜索结果
    /// </summary>
    private string FormatPropertySearchResult(string searchResult, string city, int? minPrice, int? maxPrice)
    {
        var header = $"**{city} 楼盘搜索结果**\n";

        if (minPrice.HasValue || maxPrice.HasValue)
        {
            var priceRange = minPrice.HasValue && maxPrice.HasValue
                ? $"{minPrice}-{maxPrice}万"
                : minPrice.HasValue
                    ? $"{minPrice}万以上"
                    : $"{maxPrice}万以下";
            header += $"价格区间：{priceRange}\n\n";
        }
        else
        {
            header += "\n";
        }

        return header + searchResult;
    }
}
