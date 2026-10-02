using System.Text.Json;
using System.Text.Json.Serialization;
using FoodMate.Core.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Ai;

/// <summary>
/// 基于 OpenAI 兼容接口的菜品识别客户端。
/// </summary>
/// <remarks>
/// 适用于通义千问 VL（DashScope 兼容模式）、豆包 Vision、OpenAI 等多模态模型。
/// 换供应商只需改 <c>Ai:BaseUrl</c> 与 <c>Ai:VisionModel</c>。
/// </remarks>
public sealed class OpenAiCompatibleVisionClient(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<OpenAiCompatibleVisionClient> logger)
    : OpenAiCompatibleClientBase(http, options, logger), IVisionModelClient
{
    /// <inheritdoc />
    public string ModelName => Options.VisionModel;

    /// <inheritdoc />
    public bool IsStub => false;

    /// <inheritdoc />
    public async Task<VisionRecognitionResult> RecognizeDishesAsync(
        string imageUrl,
        CancellationToken ct = default)
    {
        var (content, promptTokens, completionTokens, latency) = await CompleteAsync(
            Options.VisionModel,
            messages:
            [
                TextMessage("system", SystemPrompt),
                VisionMessage("识别这张图片里的菜品。", imageUrl),
            ],
            requireJson: true,
            ct);

        var parsed = Parse(content, logger);

        return parsed with
        {
            RawResponse = content,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            LatencyMs = latency,
        };
    }

    private const string SystemPrompt = """
        你是中餐菜品识别助手。请识别图片中所有可见的菜品。

        只返回 JSON，不要任何解释，格式如下：
        {
          "scene": "dine_in" | "takeout" | "homemade" | "unknown",
          "items": [
            {
              "name": "菜品中文常用名",
              "confidence": 0.0 到 1.0 之间的数字,
              "estimatedCalories": 估算的每份热量数字或 null,
              "ingredients": ["主要食材"],
              "portion": 份量系数，1.0 表示一份
            }
          ]
        }

        规则：
        - 菜名用中文常用叫法，不要加括号、修饰语或标点
        - confidence 表示你对这个判断的确信程度，不确定就调低
        - 只识别真正看得见的食物，不要根据场景猜测
        - 主食（米饭、面条）也要单独列出
        - 如果图片里没有食物，items 返回空数组
        """;

    private static VisionRecognitionResult Parse(string content, ILogger logger)
    {
        try
        {
            var json = ExtractJson(content);
            var dto = JsonSerializer.Deserialize<VisionDto>(json, JsonOpts);

            if (dto?.Items is null)
            {
                return VisionRecognitionResult.Empty(content);
            }

            var items = dto.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new RecognizedDishItem(
                    Name: i.Name!.Trim(),
                    Confidence: Math.Clamp(i.Confidence, 0, 1),
                    EstimatedCalories: i.EstimatedCalories,
                    Ingredients: i.Ingredients ?? [],
                    Portion: i.Portion <= 0 ? 1.0 : i.Portion))
                .ToList();

            return new VisionRecognitionResult
            {
                Items = items,
                Scene = string.IsNullOrWhiteSpace(dto.Scene) ? "unknown" : dto.Scene!,
                OverallConfidence = items.Count == 0
                    ? 0
                    : Math.Round(items.Average(i => i.Confidence), 2),
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "视觉模型返回无法解析为 JSON：{Content}", content);

            throw new AiClientException("模型返回格式无法解析。", inner: ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed record VisionDto
    {
        [JsonPropertyName("scene")]
        public string? Scene { get; init; }

        [JsonPropertyName("items")]
        public VisionItemDto[]? Items { get; init; }
    }

    private sealed record VisionItemDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; init; }

        [JsonPropertyName("estimatedCalories")]
        public int? EstimatedCalories { get; init; }

        [JsonPropertyName("ingredients")]
        public string[]? Ingredients { get; init; }

        [JsonPropertyName("portion")]
        public double Portion { get; init; } = 1.0;
    }
}
