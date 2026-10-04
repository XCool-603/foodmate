using System.Text.Json;
using System.Text.Json.Serialization;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Infrastructure.Data.Json;

namespace FoodMate.Infrastructure.Data.Seed;

/// <summary>种子数据中单道菜的 JSON 结构。</summary>
internal sealed class DishSeedItem
{
    public string Name { get; set; } = string.Empty;

    public List<string> Aliases { get; set; } = [];

    public Cuisine Cuisine { get; set; }

    public DishCategory Category { get; set; }

    public short SpicyLevel { get; set; }

    public int? PriceMinCents { get; set; }

    public int? PriceMaxCents { get; set; }

    public int? Calories { get; set; }

    public List<Ingredient> Ingredients { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public MealTimeMask MealTimes { get; set; } = MealTimeMask.All;

    public SeasonMask Seasons { get; set; } = SeasonMask.AllYear;

    public string? Description { get; set; }

    /// <summary>
    /// 菜品图片地址。
    /// </summary>
    /// <remarks>
    /// 留空时前端会渲染程序化生成的「霓虹卡面」（按菜系配色 + 按分类给图标），
    /// 因此没有图片也不会出现空占位。
    /// 想换成真实照片，把文件放进 <c>src/FoodMate.Api/wwwroot/images/dishes/</c>，
    /// 这里填 <c>/images/dishes/xxx.jpg</c> 即可。
    /// </remarks>
    public string? ImageUrl { get; set; }

    public int Popularity { get; set; }

    public RecipeSeedItem? Recipe { get; set; }
}

/// <summary>种子数据中的菜谱结构。</summary>
internal sealed class RecipeSeedItem
{
    public short Servings { get; set; } = 2;

    public short CookMinutes { get; set; }

    public short Difficulty { get; set; } = 1;

    public List<RecipeStep> Steps { get; set; } = [];

    public string? Tips { get; set; }
}

/// <summary>从内嵌资源加载内置菜品种子数据。</summary>
public static class DishSeedLoader
{
    private const string ResourceSuffix = "dishes.seed.json";

    private static readonly JsonSerializerOptions Options = new(JsonOpts.Default)
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>加载全部内置菜品（含菜谱）。</summary>
    public static IReadOnlyList<Dish> Load()
    {
        var assembly = typeof(DishSeedLoader).Assembly;

        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"找不到内嵌资源 {ResourceSuffix}，请检查 FoodMate.Infrastructure.csproj 的 EmbeddedResource 配置。");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"无法打开内嵌资源 {resourceName}。");

        var items = JsonSerializer.Deserialize<List<DishSeedItem>>(stream, Options) ?? [];

        var now = DateTimeOffset.UtcNow;

        return [.. items.Select(item => MapToDish(item, now))];
    }

    private static Dish MapToDish(DishSeedItem item, DateTimeOffset now)
    {
        var dish = new Dish
        {
            Id = Guid.NewGuid(),
            Name = item.Name,
            Aliases = [.. item.Aliases],
            Cuisine = item.Cuisine,
            Category = item.Category,
            SpicyLevel = item.SpicyLevel,
            PriceMinCents = item.PriceMinCents,
            PriceMaxCents = item.PriceMaxCents,
            Calories = item.Calories,
            Ingredients = [.. item.Ingredients],
            Tags = [.. item.Tags],
            MealTimes = item.MealTimes,
            Seasons = item.Seasons,
            Description = item.Description,
            ImageUrl = item.ImageUrl,
            Popularity = item.Popularity,
            IsBuiltin = true,
            OwnerUserId = null,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (item.Recipe is { } r)
        {
            dish.Recipe = new Recipe
            {
                Id = Guid.NewGuid(),
                DishId = dish.Id,
                Servings = r.Servings,
                CookMinutes = r.CookMinutes,
                Difficulty = r.Difficulty,
                Steps = [.. r.Steps.OrderBy(s => s.Order)],
                Tips = r.Tips,
                CreatedAt = now,
            };
        }

        return dish;
    }
}
