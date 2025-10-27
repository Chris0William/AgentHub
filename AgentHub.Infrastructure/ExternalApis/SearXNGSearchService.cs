using AgentHub.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;

namespace AgentHub.Infrastructure.ExternalApis;

/// <summary>
/// SearXNG自部署搜索服务实现 - 带缓存和智能退避
/// </summary>
public class SearXNGSearchService : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SearXNGSearchService> _logger;
    private readonly string _searxngUrl;

    // 请求节流
    private static readonly SemaphoreSlim _searchThrottle = new SemaphoreSlim(1, 1);
    private static DateTime _lastSearchTime = DateTime.MinValue;
    private static readonly TimeSpan _minSearchInterval = TimeSpan.FromMilliseconds(2500); // 最小2.5秒间隔

    // 智能退避策略
    private static int _consecutiveEmptyResults = 0; // 连续空结果计数
    private static TimeSpan _currentBackoffDelay = TimeSpan.Zero; // 当前退避延迟
    private static readonly TimeSpan _maxBackoffDelay = TimeSpan.FromSeconds(30); // 最大退避30秒

    // 搜索结果缓存（query -> (result, timestamp)）
    private static readonly ConcurrentDictionary<string, (string Result, DateTime Timestamp)> _searchCache = new();
    private static readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(30); // 缓存30分钟

    public SearXNGSearchService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SearXNGSearchService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
        _searxngUrl = configuration["ExternalApis:SearXNG:BaseUrl"] ?? "http://localhost:8080";

        // 配置HttpClient模拟真实浏览器
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/html, application/xhtml+xml, */*");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");

        _logger.LogInformation($"SearXNG搜索服务已初始化,地址: {_searxngUrl}, 请求节流: {_minSearchInterval.TotalMilliseconds}ms, 缓存过期: {_cacheExpiration.TotalMinutes}分钟");
    }

    /// <summary>
    /// 搜索网络信息（带缓存和智能退避）
    /// </summary>
    public async Task<string> SearchAsync(string query, int count = 5)
    {
        // 1. 检查缓存
        var cacheKey = $"{query}_{count}";
        if (_searchCache.TryGetValue(cacheKey, out var cached))
        {
            if (DateTime.Now - cached.Timestamp < _cacheExpiration)
            {
                _logger.LogInformation($"从缓存返回搜索结果: {query}");
                return cached.Result;
            }
            else
            {
                // 缓存过期，移除
                _searchCache.TryRemove(cacheKey, out _);
            }
        }

        // 2. 请求节流 + 智能退避
        await _searchThrottle.WaitAsync();
        try
        {
            // 基础节流延迟
            var timeSinceLastSearch = DateTime.Now - _lastSearchTime;
            var totalDelay = _minSearchInterval + _currentBackoffDelay;

            if (timeSinceLastSearch < totalDelay)
            {
                var delayMs = (int)(totalDelay - timeSinceLastSearch).TotalMilliseconds;
                _logger.LogDebug($"节流延迟: {delayMs}ms (基础: {_minSearchInterval.TotalMilliseconds}ms + 退避: {_currentBackoffDelay.TotalMilliseconds}ms)");
                await Task.Delay(delayMs);
            }
            _lastSearchTime = DateTime.Now;
        }
        finally
        {
            _searchThrottle.Release();
        }

        try
        {
            _logger.LogInformation($"SearXNG搜索: {query}");

            // 3. 发送搜索请求
            var queryParams = $"?q={Uri.EscapeDataString(query)}&format=json&language=zh";
            var requestUri = $"{_searxngUrl}/search{queryParams}";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"SearXNG搜索API调用失败: {response.StatusCode}");
                ApplyBackoff(true);
                return $"搜索失败: {response.StatusCode}";
            }

            // 4. 解析响应
            var searchResponse = await response.Content.ReadFromJsonAsync<SearXNGResponse>();
            _logger.LogInformation($"SearXNG返回结果数: {searchResponse?.Results?.Count ?? 0}, 总结果数: {searchResponse?.NumberOfResults ?? 0}");

            // 5. 检测空结果并应用退避策略
            if (searchResponse?.Results == null || !searchResponse.Results.Any())
            {
                ApplyBackoff(true);
                _logger.LogWarning($"搜索「{query}」未找到结果 (连续空结果: {_consecutiveEmptyResults}次, 当前退避: {_currentBackoffDelay.TotalSeconds}秒)。可能原因: 1)关键词过于具体 2)搜索引擎限流 3)网络问题");

                var emptyResult = $"未找到关于「{query}」的相关信息";

                // 即使是空结果也缓存，避免重复搜索
                _searchCache.TryAdd(cacheKey, (emptyResult, DateTime.Now));

                return emptyResult;
            }

            // 6. 成功获取结果，重置退避策略
            ApplyBackoff(false);

            // 7. 格式化搜索结果
            var results = new System.Text.StringBuilder();
            results.AppendLine($"搜索「{query}」找到以下信息:");
            results.AppendLine();

            int index = 1;
            foreach (var item in searchResponse.Results.Take(count))
            {
                if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Url))
                {
                    continue;
                }

                results.AppendLine($"{index}. {item.Title}");
                results.AppendLine($"   来源: {item.Url}");

                if (!string.IsNullOrWhiteSpace(item.Content))
                {
                    results.AppendLine($"   摘要: {item.Content}");
                }

                results.AppendLine();
                index++;

                if (index > count) break;
            }

            var result = results.ToString();

            // 8. 缓存成功结果
            _searchCache.TryAdd(cacheKey, (result, DateTime.Now));

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, $"SearXNG服务连接失败");
            ApplyBackoff(true);
            return $"搜索服务暂不可用: 无法连接到SearXNG";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"搜索「{query}」时发生错误");
            ApplyBackoff(true);
            return $"搜索时发生错误: {ex.Message}";
        }
    }

    /// <summary>
    /// 应用智能退避策略
    /// </summary>
    private void ApplyBackoff(bool isFailure)
    {
        if (isFailure)
        {
            _consecutiveEmptyResults++;

            // 指数退避: 2秒 -> 4秒 -> 8秒 -> 16秒 -> 30秒(最大)
            if (_consecutiveEmptyResults == 1)
            {
                _currentBackoffDelay = TimeSpan.FromSeconds(2);
            }
            else if (_consecutiveEmptyResults >= 2)
            {
                var newDelay = TimeSpan.FromSeconds(_currentBackoffDelay.TotalSeconds * 2);
                _currentBackoffDelay = newDelay > _maxBackoffDelay ? _maxBackoffDelay : newDelay;
            }

            _logger.LogWarning($"触发退避策略: 连续失败{_consecutiveEmptyResults}次, 下次搜索将额外延迟{_currentBackoffDelay.TotalSeconds}秒");
        }
        else
        {
            // 成功获取结果，重置退避
            if (_consecutiveEmptyResults > 0)
            {
                _logger.LogInformation($"搜索恢复正常，重置退避策略 (之前连续失败{_consecutiveEmptyResults}次)");
            }
            _consecutiveEmptyResults = 0;
            _currentBackoffDelay = TimeSpan.Zero;
        }
    }
}

#region 响应模型

/// <summary>
/// SearXNG搜索响应
/// </summary>
public class SearXNGResponse
{
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("number_of_results")]
    public int NumberOfResults { get; set; }

    [JsonPropertyName("results")]
    public List<SearXNGResult>? Results { get; set; }
}

/// <summary>
/// SearXNG单个搜索结果
/// </summary>
public class SearXNGResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("parsed_url")]
    public List<string>? ParsedUrl { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }
}

#endregion
