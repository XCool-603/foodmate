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

    /// <summary>
    /// 是否适合在家做。默认 true —— 绝大多数菜都能做，
    /// 只把佛跳墙、烤鸭这类餐厅专属的标为 false。
    /// </summary>
    public bool CanMakeAtHome { get; set; } = true;

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
/// <remarks>
/// 支持<b>多个</b>种子文件：凡是资源名里含 <c>dishes.seed</c> 且以 <c>.json</c> 结尾的都会被加载。
/// 这样可以把「带完整菜谱的核心菜品」和「只含基础信息的扩充菜品」分开放，
/// 各自文件保持在可人工维护的规模。
/// </remarks>
public static class DishSeedLoader
{
    private const string ResourceMarker = "dishes.seed";

    private static readonly JsonSerializerOptions Options = new(JsonOpts.Default)
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>加载全部内置菜品（含菜谱）。</summary>
    public static IReadOnlyList<Dish> Load()
    {
        var assembly = typeof(DishSeedLoader).Assembly;

        var resourceNames = Array.FindAll(
            assembly.GetManifestResourceNames(),
            n => n.Contains(ResourceMarker, StringComparison.OrdinalIgnoreCase)
                 && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException(
                $"找不到内嵌资源 {ResourceMarker}*.json，请检查 FoodMate.Infrastructure.csproj 的 EmbeddedResource 配置。");
        }

        // 固定顺序，保证每次启动的导入顺序一致（便于排查问题）
        Array.Sort(resourceNames, StringComparer.Ordinal);

        var items = new List<DishSeedItem>();

        foreach (var name in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"无法打开内嵌资源 {name}。");

            var batch = JsonSerializer.Deserialize<List<DishSeedItem>>(stream, Options) ?? [];
            items.AddRange(batch);
        }

        var now = DateTimeOffset.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dishes = new List<Dish>(items.Count);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            // 同名菜品只保留先加载的那个 —— 后加载的文件可以覆盖式补充，但不会产生重复
            if (!seen.Add(item.Name))
            {
                continue;
            }

            dishes.Add(MapToDish(item, now));
        }

        return dishes;
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
            CanMakeAtHome = item.CanMakeAtHome,
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
