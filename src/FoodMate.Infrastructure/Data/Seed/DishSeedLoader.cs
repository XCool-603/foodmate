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

        // 按菜名合并，而不是「先到先得丢弃」——
        // 这样「基础信息」和「菜谱」可以分文件维护，多个文件也能并行产出而互不冲突。
        var byName = new Dictionary<string, DishSeedItem>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>(items.Count);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            if (!byName.TryGetValue(item.Name, out var existing))
            {
                byName[item.Name] = item;
                order.Add(item.Name);
                continue;
            }

            // 同名合并策略：**菜谱允许后加载的覆盖，其余字段只补空缺**。
            //
            // 为什么菜谱要特殊对待：`dishes.seed.recipes.json` 排在资源序最后，
            // 它的存在意义就是「补齐并修正」已有菜谱（例如把只有 2 步的菜谱补到 3 步）。
            // 若也用 ??=，它永远覆盖不掉先加载文件里的旧菜谱，整条工作流就是白干。
            // 其余字段（价格、热量、食材等）仍保持「只补空缺」，避免补充文件
            // 无意中改掉基础数据。
            if (item.Recipe is not null)
            {
                existing.Recipe = item.Recipe;
            }

            existing.Description ??= item.Description;
            existing.ImageUrl ??= item.ImageUrl;
            existing.PriceMinCents ??= item.PriceMinCents;
            existing.PriceMaxCents ??= item.PriceMaxCents;
            existing.Calories ??= item.Calories;

            if (existing.Aliases.Count == 0) existing.Aliases = item.Aliases;
            if (existing.Tags.Count == 0) existing.Tags = item.Tags;
            if (existing.Ingredients.Count == 0) existing.Ingredients = item.Ingredients;
        }

        // 拦掉「孤儿补充项」：只写了菜谱、没有任何基础信息的条目，
        // 如果它的菜名从未命中任何基础定义，那几乎一定是**菜名打错**。
        //
        // 为什么必须拦：`MapToDish` 会把它落成一道
        // 「Cuisine=Other / Category=Unknown / 无食材 / 无价格」的空菜品。
        // 更糟的是菜品总数会因此**变大**，让「≥300 道」这类指标看起来更达标，
        // 错误被完全掩盖。宁可启动就失败，也不要悄悄污染菜品库。
        var orphanRecipes = order
            .Where(name => IsSupplementOnly(byName[name]))
            .ToList();

        if (orphanRecipes.Count > 0)
        {
            throw new InvalidOperationException(
                "种子文件里有菜谱补充项的菜名在菜品库中不存在，请检查是否拼写错误：" +
                string.Join("、", orphanRecipes));
        }

        return [.. order.Select(name => MapToDish(byName[name], now))];
    }

    /// <summary>
    /// 是否是「只补菜谱」的条目 —— 有菜谱，但没有任何基础信息。
    /// </summary>
    /// <remarks>
    /// 判据用「食材为空且热度为 0」：所有真实菜品定义都至少有 2 项食材，
    /// 而补充文件按约定只写 <c>name</c> + <c>recipe</c>。
    /// </remarks>
    private static bool IsSupplementOnly(DishSeedItem item) =>
        item.Recipe is not null
        && item.Ingredients.Count == 0
        && item.Popularity == 0;

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
