using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodMate.Core.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Ai;

/// <summary>
/// OpenAI 兼容接口的公共部分。
/// </summary>
/// <remarks>
/// 通义千问（DashScope 兼容模式）、豆包、DeepSeek、OpenAI 等都实现了
/// <c>/chat/completions</c> 这一套协议，因此一个客户端可以对接多家，
/// 换供应商只需改配置里的 <c>BaseUrl</c> 与模型名。
/// </remarks>
public abstract class OpenAiCompatibleClientBase(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger logger)
{
    /// <summary>AI 配置。</summary>
    protected AiOptions Options { get; } = options.Value;

    /// <summary>日志。</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>发起一次对话补全并返回模型输出的文本内容。</summary>
    protected async Task<(string Content, int? PromptTokens, int? CompletionTokens, long LatencyMs)>
        CompleteAsync(
            string model,
            object[] messages,
            bool requireJson,
            CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Options.BaseUrl))
        {
            throw new AiClientException(
                "未配置 Ai:BaseUrl。若只想本地跑通流程，请把 Ai:Provider 设为 Stub。");
        }

        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            throw new AiClientException(
                "未配置 Ai:ApiKey。请通过环境变量或用户机密注入，切勿写入仓库。");
        }

        var payload = new ChatRequest
        {
            Model = model,
            Messages = messages,
            Temperature = 0.1,
            ResponseFormat = requireJson ? new ChatResponseFormat { Type = "json_object" } : null,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri())
        {
            Content = JsonContent.Create(payload),
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);

        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;

        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiClientException(
                $"模型调用超时（{Options.TimeoutSeconds}s）。", isTimeout: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiClientException($"模型服务不可达：{ex.Message}", inner: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogWarning(
                    "模型返回 {Status}：{Body}", (int)response.StatusCode, Truncate(body, 500));

                throw new AiClientException(
                    $"模型服务返回 {(int)response.StatusCode}。");
            }

            var parsed = JsonSerializer.Deserialize<ChatResponse>(body, JsonOpts);

            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new AiClientException("模型返回内容为空。");
            }

            return (
                content,
                parsed?.Usage?.PromptTokens,
                parsed?.Usage?.CompletionTokens,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// 从模型输出中提取 JSON。
    /// </summary>
    /// <remarks>
    /// 即使要求了 <c>json_object</c>，模型仍可能把 JSON 包在
    /// <c>```json</c> 代码块里，或前后带上解释性文字。这里做容错提取。
    /// </remarks>
    protected static string ExtractJson(string content)
    {
        var text = content.Trim();

        // 去掉 Markdown 代码块围栏
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd > 0)
            {
                text = text[(firstLineEnd + 1)..];
            }

            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                text = text[..fenceEnd];
            }

            text = text.Trim();
        }

        // 截取第一个 { 到最后一个 }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            return text[start..(end + 1)];
        }

        return text;
    }

    private Uri BuildUri()
    {
        var baseUrl = Options.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/chat/completions");
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    /// <summary>构造一条纯文本消息。</summary>
    protected static object TextMessage(string role, string content)
        => new { role, content };

    /// <summary>构造一条「文本 + 图片」的多模态消息。</summary>
    protected static object VisionMessage(string text, string imageUrl)
        => new
        {
            role = "user",
            content = new object[]
            {
                new { type = "text", text },
                new { type = "image_url", image_url = new { url = imageUrl } },
            },
        };

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── 请求/响应契约 ───────────────────────────────────────

    private sealed record ChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required object[] Messages { get; init; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("response_format")]
        public ChatResponseFormat? ResponseFormat { get; init; }
    }

    private sealed record ChatResponseFormat
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }
    }

    private sealed record ChatResponse
    {
        [JsonPropertyName("choices")]
        public ChatChoice[]? Choices { get; init; }

        [JsonPropertyName("usage")]
        public ChatUsage? Usage { get; init; }
    }

    private sealed record ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; init; }
    }

    private sealed record ChatMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed record ChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; init; }
    }
}
