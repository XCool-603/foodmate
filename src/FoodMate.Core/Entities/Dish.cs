using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 菜品库。既含平台内置菜（<see cref="IsBuiltin"/> = true），也含用户自定义菜。
/// </summary>
public sealed class Dish
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>别名，用于搜索与 AI 识别匹配。</summary>
    public List<string> Aliases { get; set; } = [];

    public Cuisine Cuisine { get; set; }

    public DishCategory Category { get; set; }

    /// <summary>辣度 0–5。</summary>
    public short SpicyLevel { get; set; }

    public int? PriceMinCents { get; set; }

    public int? PriceMaxCents { get; set; }

    /// <summary>每份估算热量（kcal）。</summary>
    public int? Calories { get; set; }

    public List<Ingredient> Ingredients { get; set; } = [];

    /// <summary>标签，如「下饭」「暖胃」「快手」。</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>适用餐次位掩码。</summary>
    public MealTimeMask MealTimes { get; set; } = MealTimeMask.All;

    /// <summary>适用季节位掩码；<see cref="SeasonMask.AllYear"/> 表示四季皆宜。</summary>
    public SeasonMask Seasons { get; set; } = SeasonMask.AllYear;

    public string? ImageUrl { get; set; }

    public string? Description { get; set; }

    /// <summary>true = 平台内置；false = 用户自定义。</summary>
    public bool IsBuiltin { get; set; } = true;

    /// <summary>用户自定义菜品的归属用户；内置菜为 null。</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>全局热度 0–100，用于冷启动兜底排序。</summary>
    public int Popularity { get; set; }

    /// <summary>
    /// 是否适合在家做。
    /// </summary>
    /// <remarks>
    /// 「自己做」模式的硬过滤用这个字段，<b>而不是</b>「有没有缓存菜谱」——
    /// 两者是不同的事：
    ///   · 佛跳墙、烤鸭 → 家里做不了，该排除
    ///   · 蒜蓉西兰花 → 只是还没录菜谱，但随时可以按需生成，不该排除
    /// 默认 true（绝大多数家常菜都能做），只把餐厅专属的菜标为 false。
    /// </remarks>
    public bool CanMakeAtHome { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? Owner { get; set; }

    public Recipe? Recipe { get; set; }
}
