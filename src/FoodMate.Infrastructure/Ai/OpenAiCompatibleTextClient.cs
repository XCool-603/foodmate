using System.Text.Json;
using System.Text.Json.Serialization;
using FoodMate.Core.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Ai;

/// <summary>基于 OpenAI 兼容接口的文本生成客户端（菜谱、买菜清单）。</summary>
public sealed class OpenAiCompatibleTextClient(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<OpenAiCompatibleTextClient> logger)
    : OpenAiCompatibleClientBase(http, options, logger), ITextModelClient
{
    /// <inheritdoc />
    public string ModelName => Options.TextModel;

    /// <inheritdoc />
    public bool IsStub => false;

    /// <inheritdoc />
    public async Task<GeneratedRecipe> GenerateRecipeAsync(
        string dishName,
        short servings,
        CancellationToken ct = default)
    {
        // 用 $$""" 而非 $"""：这样 {{...}} 才是插值占位符，单个 { 是字面量，
        // 可以直接书写 JSON 示例而不必到处转义
        var prompt = $$"""
            为「{{dishName}}」写一份家常菜谱，{{servings}} 人份。

            只返回 JSON，不要任何解释：
            {
              "servings": {{servings}},
              "cookMinutes": 总时长分钟数,
              "difficulty": 1 到 3,
              "steps": [ { "order": 1, "text": "步骤描述", "durationMinutes": 分钟数 } ],
              "tips": "一句话小贴士"
            }

            要求：
            - 步骤要具体可执行，包含火候、时间、用量
            - 步骤数量控制在 4 到 8 步
            - 不要出现「适量」「少许」这类无法执行的描述，给出具体用量
            """;

        var (content, _, _, _) = await CompleteAsync(
            Options.TextModel,
            messages: [TextMessage("user", prompt)],
            requireJson: true,
            ct);

        return ParseRecipe(content, dishName, servings, logger);
    }

    /// <inheritdoc />
    public async Task<ShoppingList> GenerateShoppingListAsync(
        IReadOnlyList<string> dishNames,
        short servings,
        CancellationToken ct = default)
    {
        var dishes = string.Join("、", dishNames);

        var prompt = $$"""
            我要做这几道菜：{{dishes}}，共 {{servings}} 人份。

            请生成一份买菜清单，同类食材要合并。

            只返回 JSON，不要任何解释：
            {
              "items": [
                {
                  "name": "食材名",
                  "totalAmount": "合计用量，如 500g",
                  "category": "蔬菜" | "肉类" | "水产" | "蛋奶" | "调味" | "主食" | "其他",
                  "forDishes": ["用于哪道菜"]
                }
              ],
              "textSummary": "按分类分组的纯文本清单，便于复制"
            }

            要求：
            - 用量要具体，不要「适量」
            - 葱姜蒜这类共用配料合并成一条
            - textSummary 用「【分类】食材 用量」的格式，每行一类
            """;

        var (content, _, _, _) = await CompleteAsync(
            Options.TextModel,
            messages: [TextMessage("user", prompt)],
            requireJson: true,
            ct);

        return ParseShoppingList(content, logger);
    }

    private static GeneratedRecipe ParseRecipe(
        string content,
        string dishName,
        short servings,
        ILogger logger)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<RecipeDto>(ExtractJson(content), JsonOpts);

            if (dto?.Steps is null || dto.Steps.Length == 0)
            {
                throw new AiClientException("模型未返回菜谱步骤。");
            }

            return new GeneratedRecipe
            {
                DishName = dishName,
                Servings = dto.Servings > 0 ? dto.Servings : servings,
                CookMinutes = dto.CookMinutes,
                Difficulty = (short)Math.Clamp((int)dto.Difficulty, 1, 3),
                Steps =
                [
                    .. dto.Steps
                        .OrderBy(s => s.Order)
                        .Select((s, i) => new GeneratedRecipeStep(
                            s.Order > 0 ? s.Order : i + 1,
                            s.Text ?? string.Empty,
                            s.DurationMinutes)),
                ],
                Tips = dto.Tips,
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "菜谱生成返回无法解析：{Content}", content);
            throw new AiClientException("菜谱生成返回格式无法解析。", inner: ex);
        }
    }

    private static ShoppingList ParseShoppingList(string content, ILogger logger)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<ShoppingListDto>(ExtractJson(content), JsonOpts);

            if (dto?.Items is null)
            {
                throw new AiClientException("模型未返回买菜清单。");
            }

            var items = dto.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .Select(i => new ShoppingListItem(
                    i.Name!.Trim(),
                    i.TotalAmount,
                    i.Category,
                    i.ForDishes ?? []))
                .ToList();

            return new ShoppingList
            {
                Items = items,
                TextSummary = dto.TextSummary ?? string.Empty,
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "买菜清单返回无法解析：{Content}", content);
            throw new AiClientException("买菜清单返回格式无法解析。", inner: ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed record RecipeDto
    {
        [JsonPropertyName("servings")]
        public short Servings { get; init; }

        [JsonPropertyName("cookMinutes")]
        public short CookMinutes { get; init; }

        [JsonPropertyName("difficulty")]
        public short Difficulty { get; init; } = 1;

        [JsonPropertyName("steps")]
        public RecipeStepDto[]? Steps { get; init; }

        [JsonPropertyName("tips")]
        public string? Tips { get; init; }
    }

    private sealed record RecipeStepDto
    {
        [JsonPropertyName("order")]
        public int Order { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("durationMinutes")]
        public int? DurationMinutes { get; init; }
    }

    private sealed record ShoppingListDto
    {
        [JsonPropertyName("items")]
        public ShoppingItemDto[]? Items { get; init; }

        [JsonPropertyName("textSummary")]
        public string? TextSummary { get; init; }
    }

    private sealed record ShoppingItemDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("totalAmount")]
        public string? TotalAmount { get; init; }

        [JsonPropertyName("category")]
        public string? Category { get; init; }

        [JsonPropertyName("forDishes")]
        public string[]? ForDishes { get; init; }
    }
}
