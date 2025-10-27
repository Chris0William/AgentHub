using AgentHub.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentHub.Infrastructure.AI;

/// <summary>
/// 千问LLM服务实现
/// </summary>
public class QwenService : ILLMService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<QwenService> _logger;
    private readonly string _apiKey;
    private readonly string _apiEndpoint;
    private readonly string _model;

    public QwenService(HttpClient httpClient, IConfiguration configuration, ILogger<QwenService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _apiKey = _configuration["AI:Qwen:ApiKey"] ?? throw new InvalidOperationException("千问API密钥未配置");
        var baseEndpoint = _configuration["AI:Qwen:ApiEndpoint"] ?? "https://dashscope.aliyuncs.com/compatible-mode/v1";
        _apiEndpoint = $"{baseEndpoint.TrimEnd('/')}/chat/completions";
        _model = _configuration["AI:Qwen:Model"] ?? "qwen-max";

        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <summary>
    /// 发送聊天消息(非流式)
    /// </summary>
    public async Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var request = new QwenChatRequest
        {
            Model = _model,
            Messages = messages.Select(m => new QwenMessage
            {
                Role = m.Role,
                Content = m.Content
            }).ToList(),
            Stream = false
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(_apiEndpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<QwenChatResponse>(cancellationToken: cancellationToken);

            if (result?.Choices == null || result.Choices.Count == 0)
            {
                throw new InvalidOperationException("千问API返回空响应");
            }

            return result.Choices[0].Message?.Content ?? string.Empty;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用千问API失败");
            throw new InvalidOperationException("调用AI服务失败", ex);
        }
    }

    /// <summary>
    /// 发送聊天消息(流式)
    /// </summary>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new QwenChatRequest
        {
            Model = _model,
            Messages = messages.Select(m => new QwenMessage
            {
                Role = m.Role,
                Content = m.Content
            }).ToList(),
            Stream = true
        };

        var jsonContent = JsonSerializer.Serialize(request);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(_apiEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用千问API失败");
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
                continue;

            var data = line.Substring(6).Trim();

            if (data == "[DONE]")
                break;

            QwenStreamResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<QwenStreamResponse>(data);
            }
            catch (JsonException)
            {
                continue;
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            if (choice?.Delta?.Content != null)
            {
                yield return choice.Delta.Content;
            }
        }
    }
}

// ===== 千问API请求/响应模型 =====

internal class QwenChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<QwenMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal class QwenMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal class QwenChatResponse
{
    [JsonPropertyName("choices")]
    public List<QwenChoice> Choices { get; set; } = new();
}

internal class QwenChoice
{
    [JsonPropertyName("message")]
    public QwenMessage? Message { get; set; }
}

internal class QwenStreamResponse
{
    [JsonPropertyName("choices")]
    public List<QwenStreamChoice>? Choices { get; set; }
}

internal class QwenStreamChoice
{
    [JsonPropertyName("delta")]
    public QwenDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class QwenDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
