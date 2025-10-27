using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentHub.Infrastructure.ExternalApis;

/// <summary>
/// 聚合数据星座运势API服务实现
/// </summary>
public class JuheHoroscopeService : IJuheHoroscopeService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<JuheHoroscopeService> _logger;
    private readonly string _apiKey;
    private readonly string _apiEndpoint;

    public JuheHoroscopeService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<JuheHoroscopeService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _apiKey = _configuration["ExternalApis:Juhe:HoroscopeApiKey"]
            ?? throw new InvalidOperationException("聚合数据星座运势API密钥未配置");
        _apiEndpoint = "http://web.juhe.cn:8080/constellation/getAll";
    }

    /// <summary>
    /// 获取星座运势
    /// </summary>
    public async Task<string> GetHoroscopeAsync(string constellation, string type = "today")
    {
        try
        {
            var requestUrl = $"{_apiEndpoint}?consName={constellation}&type={type}&key={_apiKey}";

            _logger.LogInformation($"调用聚合数据星座运势API: {requestUrl.Replace(_apiKey, "***")}");

            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JuheHoroscopeResponse>(content);

            if (result?.ErrorCode != 0)
            {
                _logger.LogWarning($"聚合数据星座运势API返回错误: {result?.Reason}");
                return GetFallbackHoroscope(constellation, type);
            }

            if (result.Data == null)
            {
                _logger.LogWarning("聚合数据星座运势API返回空结果");
                return GetFallbackHoroscope(constellation, type);
            }

            // 格式化返回结果
            return FormatHoroscope(constellation, type, result.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调用聚合数据星座运势API失败");
            // 如果API调用失败，返回备用数据
            return GetFallbackHoroscope(constellation, type);
        }
    }

    /// <summary>
    /// 格式化星座运势信息
    /// </summary>
    private string FormatHoroscope(string constellation, string type, JuheHoroscopeData data)
    {
        var typeText = type switch
        {
            "today" => "今日",
            "tomorrow" => "明日",
            "week" => "本周",
            "month" => "本月",
            "year" => "本年",
            _ => type
        };

        var result = $"【{constellation} {typeText}运势】\n\n";

        result += $"📅 日期: {data.Date ?? "N/A"}\n";
        result += $"💫 综合运势: {data.All ?? "未知"}\n";
        result += $"💼 工作运势: {data.Work ?? "未知"}\n";
        result += $"💰 财富运势: {data.Money ?? "未知"}\n";
        result += $"💑 爱情运势: {data.Love ?? "未知"}\n";
        result += $"💪 健康运势: {data.Health ?? "未知"}\n\n";

        if (!string.IsNullOrEmpty(data.Summary))
        {
            result += $"📝 运势简评:\n{data.Summary}\n\n";
        }

        result += $"🎨 幸运颜色: {data.Color ?? "未知"}\n";
        result += $"🔢 幸运数字: {data.Number ?? "未知"}\n";
        result += $"🌟 速配星座: {data.QFriend ?? "未知"}";

        return result;
    }

    /// <summary>
    /// 获取备用星座运势（当API调用失败时使用）
    /// </summary>
    private string GetFallbackHoroscope(string constellation, string type)
    {
        var today = DateTime.Now;
        var seed = today.Year * 10000 + today.Month * 100 + today.Day + constellation.GetHashCode();
        var random = new Random(seed);

        var typeText = type switch
        {
            "today" => "今日",
            "tomorrow" => "明日",
            "week" => "本周",
            "month" => "本月",
            "year" => "本年",
            _ => type
        };

        var ratings = new[] { "★★★★★", "★★★★☆", "★★★☆☆", "★★★★☆", "★★★★★" };
        var colors = new[] { "紫色", "蓝色", "绿色", "红色", "黄色", "橙色", "粉色" };
        var numbers = Enumerable.Range(1, 9).OrderBy(x => random.Next()).Take(1).ToArray();

        var summaries = new[]
        {
            "今天是充满机遇的一天，保持积极的心态将会带来好运。",
            "注意与他人的沟通，耐心倾听会让你收获良多。",
            "财运不错，但要注意理性消费，避免冲动购物。",
            "工作中可能会遇到一些挑战，但你有能力克服。",
            "感情运势上扬，单身者有机会遇到心仪的对象。",
            "今天适合休息调整，给自己一些放松的时间。"
        };

        var result = $"【{constellation} {typeText}运势】\n\n";
        result += $"📅 日期: {today:yyyy年MM月dd日}\n";
        result += $"💫 综合运势: {ratings[random.Next(ratings.Length)]}\n";
        result += $"💼 工作运势: {ratings[random.Next(ratings.Length)]}\n";
        result += $"💰 财富运势: {ratings[random.Next(ratings.Length)]}\n";
        result += $"💑 爱情运势: {ratings[random.Next(ratings.Length)]}\n";
        result += $"💪 健康运势: {ratings[random.Next(ratings.Length)]}\n\n";
        result += $"📝 运势简评:\n{summaries[random.Next(summaries.Length)]}\n\n";
        result += $"🎨 幸运颜色: {colors[random.Next(colors.Length)]}\n";
        result += $"🔢 幸运数字: {numbers[0]}\n";
        result += $"🌟 友情提示: 本数据为系统生成，仅供参考";

        return result;
    }
}

// ===== 聚合数据API响应模型 =====

internal class JuheHoroscopeResponse
{
    [JsonPropertyName("error_code")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("data")]
    public JuheHoroscopeData? Data { get; set; }
}

internal class JuheHoroscopeData
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("all")]
    public string? All { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("health")]
    public string? Health { get; set; }

    [JsonPropertyName("love")]
    public string? Love { get; set; }

    [JsonPropertyName("money")]
    public string? Money { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("QFriend")]
    public string? QFriend { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("work")]
    public string? Work { get; set; }
}
