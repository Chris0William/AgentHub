using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentHub.Infrastructure.ExternalApis;

/// <summary>
/// 聚合数据老黄历API服务实现
/// </summary>
public class JuheCalendarService : IJuheCalendarService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JuheCalendarService> _logger;
    private readonly string _apiKey;
    private readonly string _apiEndpoint;

    public JuheCalendarService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<JuheCalendarService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _apiKey = _configuration["ExternalApis:Juhe:CalendarApiKey"]
            ?? throw new InvalidOperationException("聚合数据老黄历API密钥未配置");
        _apiEndpoint = "http://v.juhe.cn/laohuangli/d";
    }

    /// <summary>
    /// 获取今日宜忌
    /// </summary>
    public async Task<string> GetTodayTaboosAsync()
    {
        try
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var requestUrl = $"{_apiEndpoint}?date={today}&key={_apiKey}";

            _logger.LogInformation($"调用聚合数据老黄历API: {requestUrl.Replace(_apiKey, "***")}");

            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JuheCalendarResponse>(content);

            if (result?.ErrorCode != 0)
            {
                _logger.LogWarning($"聚合数据老黄历API返回错误: {result?.Reason}");
                return GetFallbackTaboos();
            }

            if (result.Result == null)
            {
                _logger.LogWarning("聚合数据老黄历API返回空结果");
                return GetFallbackTaboos();
            }

            // 格式化返回结果
            var yi = result.Result.Yi ?? "暂无";
            var ji = result.Result.Ji ?? "暂无";

            return $"今日宜:{yi.Replace(" ", "、")}\n今日忌:{ji.Replace(" ", "、")}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用聚合数据老黄历API失败");
            // 如果API调用失败，返回备用数据
            return GetFallbackTaboos();
        }
    }

    /// <summary>
    /// 获取备用宜忌数据（当API调用失败时使用）
    /// </summary>
    private string GetFallbackTaboos()
    {
        var today = DateTime.Now;
        var seed = today.Year * 10000 + today.Month * 100 + today.Day;
        var random = new Random(seed);

        var suitable = new[] { "嫁娶", "祭祀", "祈福", "求嗣", "出行", "解除", "伐木", "入宅", "移徙", "安床", "开市", "交易", "立券", "栽种" };
        var avoid = new[] { "动土", "破土", "掘井", "安葬", "修造", "上梁", "开池", "造船", "纳畜", "造畜椆栖" };

        var suitableCount = random.Next(3, 6);
        var avoidCount = random.Next(2, 5);

        var todaySuitable = suitable.OrderBy(x => random.Next()).Take(suitableCount).ToArray();
        var todayAvoid = avoid.OrderBy(x => random.Next()).Take(avoidCount).ToArray();

        return $"今日宜:{string.Join("、", todaySuitable)}\n今日忌:{string.Join("、", todayAvoid)}";
    }
}

// ===== 聚合数据API响应模型 =====

internal class JuheCalendarResponse
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("result")]
    public JuheCalendarResult? Result { get; set; }

    [JsonPropertyName("error_code")]
    public int ErrorCode { get; set; }
}

internal class JuheCalendarResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("yangli")]
    public string? Yangli { get; set; }

    [JsonPropertyName("yinli")]
    public string? Yinli { get; set; }

    [JsonPropertyName("wuxing")]
    public string? Wuxing { get; set; }

    [JsonPropertyName("chongsha")]
    public string? Chongsha { get; set; }

    [JsonPropertyName("baiji")]
    public string? Baiji { get; set; }

    [JsonPropertyName("jishen")]
    public string? Jishen { get; set; }

    [JsonPropertyName("yi")]
    public string? Yi { get; set; }

    [JsonPropertyName("xiongshen")]
    public string? Xiongshen { get; set; }

    [JsonPropertyName("ji")]
    public string? Ji { get; set; }
}
