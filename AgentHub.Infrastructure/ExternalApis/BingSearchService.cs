using AgentHub.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AgentHub.Infrastructure.ExternalApis;

/// <summary>
/// Bing Web Search API服务实现
/// </summary>
public class BingSearchService : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BingSearchService> _logger;
    private readonly string _apiKey;
    private readonly string _endpoint = "https://api.bing.microsoft.com/v7.0/search";

    public BingSearchService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BingSearchService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
        _apiKey = configuration["ExternalApis:Bing:ApiKey"] ?? "";

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Bing Search API Key未配置，搜索功能将不可用");
        }
        else
        {
            _logger.LogInformation("Bing搜索服务已初始化");
        }
    }

    public async Task<string> SearchAsync(string query, int count = 5)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return "搜索服务未配置API Key";
        }

        try
        {
            _logger.LogInformation($"Bing搜索: {query}");

            var requestUri = $"{_endpoint}?q={Uri.EscapeDataString(query)}&count={count}&mkt=zh-CN";
            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Bing搜索API调用失败: {response.StatusCode}");
                return $"搜索失败: {response.StatusCode}";
            }

            var searchResponse = await response.Content.ReadFromJsonAsync<BingSearchResponse>();

            if (searchResponse?.WebPages?.Value == null || !searchResponse.WebPages.Value.Any())
            {
                _logger.LogWarning($"搜索「{query}」未找到结果");
                return $"未找到关于「{query}」的相关信息";
            }

            var results = new System.Text.StringBuilder();
            results.AppendLine($"搜索「{query}」找到以下信息:");
            results.AppendLine();

            int index = 1;
            foreach (var item in searchResponse.WebPages.Value.Take(count))
            {
                results.AppendLine($"{index}. {item.Name}");
                results.AppendLine($"   来源: {item.Url}");

                if (!string.IsNullOrWhiteSpace(item.Snippet))
                {
                    results.AppendLine($"   摘要: {item.Snippet}");
                }

                results.AppendLine();
                index++;
            }

            _logger.LogInformation($"Bing搜索成功，返回{searchResponse.WebPages.Value.Count}条结果");
            return results.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"搜索「{query}」时发生错误");
            return $"搜索时发生错误: {ex.Message}";
        }
    }
}

public class BingSearchResponse
{
    [JsonPropertyName("webPages")]
    public WebPages? WebPages { get; set; }
}

public class WebPages
{
    [JsonPropertyName("value")]
    public List<WebPage>? Value { get; set; }
}

public class WebPage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = "";
}
