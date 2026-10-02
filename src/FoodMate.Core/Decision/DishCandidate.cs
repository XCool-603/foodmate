using FoodMate.Core.Entities;
using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision;

/// <summary>
/// 参与打分的菜品视图。由 Application 层从实体展开 JSON 字段后构造。
/// </summary>
public sealed record DishCandidate
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Cuisine Cuisine { get; init; }

    public required DishCategory Category { get; init; }

    public required short SpicyLevel { get; init; }

    public int? PriceMinCents { get; init; }

    public int? PriceMaxCents { get; init; }

    public int? Calories { get; init; }

    public IReadOnlyList<Ingredient> Ingredients { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public MealTimeMask MealTimes { get; init; } = MealTimeMask.All;

    public SeasonMask Seasons { get; init; } = SeasonMask.AllYear;

    /// <summary>是否有菜谱（「自己做」模式的硬性前提）。</summary>
    public bool HasRecipe { get; init; }

    /// <summary>烹饪时长（分钟）。</summary>
    public short? CookMinutes { get; init; }

    /// <summary>全局热度 0–100，冷启动兜底用。</summary>
    public int Popularity { get; init; }

    /// <summary>true = 平台内置；false = 用户自定义。</summary>
    public bool IsBuiltin { get; init; } = true;

    /// <summary>堂食 / 外卖模式下的距离（公里）；未知为 null。</summary>
    public double? DistanceKm { get; init; }

    /// <summary>该菜适合的就餐方式（用于「随便」模式下的偏好加权）。</summary>
    public DiningMode PreferredDiningMode => HasRecipe ? DiningMode.Homemade : DiningMode.DineIn;

    /// <summary>参考价（分）。区间取中值；缺失时按分类兜底。</summary>
    public int ReferencePriceCents
    {
        get
        {
            if (PriceMinCents is null && PriceMaxCents is null)
            {
                return CategoryDefaultPrice.For(Category);
            }

            if (PriceMinCents is { } min && PriceMaxCents is { } max)
            {
                return (min + max) / 2;
            }

            return PriceMinCents ?? PriceMaxCents!.Value;
        }
    }
}

/// <summary>用户画像快照。</summary>
public sealed record UserPreferenceSnapshot
{
    /// <summary>辣度偏好 0–5。</summary>
    public short SpicyLevel { get; init; } = 2;

    /// <summary>预算下限（分）。</summary>
    public int BudgetMinCents { get; init; } = 1500;

    /// <summary>预算上限（分）。</summary>
    public int BudgetMaxCents { get; init; } = 5000;

    /// <summary>忌口 / 过敏食材。<b>硬过滤条件</b>。</summary>
    public IReadOnlyList<string> AvoidIngredients { get; init; } = [];

    /// <summary>偏好菜系。</summary>
    public IReadOnlyList<Cuisine> PreferredCuisines { get; init; } = [];

    /// <summary>就餐方式偏好权重。</summary>
    public DiningModeWeights DiningModeWeights { get; init; } = new();

    /// <summary>可接受距离（米）。</summary>
    public int MaxDistanceM { get; init; } = 2000;

    /// <summary>未做偏好引导时的默认画像。</summary>
    public static UserPreferenceSnapshot Default { get; } = new();
}

/// <summary>用户 × 菜品历史快照。</summary>
/// <param name="EatCount">累计食用次数。</param>
/// <param name="EatCount30d">近 30 天食用次数（新鲜度频次项用）。</param>
/// <param name="LastEatenAt">最近一次食用时间。</param>
/// <param name="RatingSum">评分总和。</param>
/// <param name="RatingCount">评分次数。</param>
/// <param name="WouldEatAgainCount">「还想再吃」次数。</param>
/// <param name="WouldNotEatAgainCount">明确「不想再吃」次数。</param>
public sealed record DishStatSnapshot(
    int EatCount,
    int EatCount30d,
    DateTimeOffset? LastEatenAt,
    int RatingSum,
    int RatingCount,
    int WouldEatAgainCount,
    int WouldNotEatAgainCount = 0)
{
    /// <summary>从未吃过。</summary>
    public static DishStatSnapshot Empty { get; } = new(0, 0, null, 0, 0, 0);

    /// <summary>平均评分；无评分时为 null。</summary>
    public double? AverageRating => RatingCount > 0 ? (double)RatingSum / RatingCount : null;

    /// <summary>「还想再吃」占比；无记录时为 null。</summary>
    public double? WouldEatAgainRate => EatCount > 0 ? (double)WouldEatAgainCount / EatCount : null;

    /// <summary>用户是否明确表示过不想再吃（说过「不」且从未说过「是」）。</summary>
    public bool IsNeverAgain => WouldNotEatAgainCount > 0 && WouldEatAgainCount == 0;
}

/// <summary>用户 × 菜系历史快照。</summary>
/// <param name="EatCountTotal">累计食用次数。</param>
/// <param name="EatCount30d">近 30 天食用次数。</param>
/// <param name="LastEatenAt">最近一次食用时间。</param>
public sealed record CuisineStatSnapshot(
    int EatCountTotal,
    int EatCount30d,
    DateTimeOffset? LastEatenAt)
{
    /// <summary>从未吃过该菜系。</summary>
    public static CuisineStatSnapshot Empty { get; } = new(0, 0, null);
}
