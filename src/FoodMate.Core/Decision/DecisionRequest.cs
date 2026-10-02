using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision;

/// <summary>经纬度坐标。</summary>
/// <param name="Latitude">纬度。</param>
/// <param name="Longitude">经度。</param>
public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>
/// 决策请求：用户本次的显式意图。
/// </summary>
/// <remarks>
/// 所有条件<b>都有默认值</b>——用户一个都不改也必须能出结果。
/// </remarks>
public sealed record DecisionRequest
{
    /// <summary>餐次；null 时按 <see cref="Now"/> + <see cref="LocalOffset"/> 推断。</summary>
    public MealType? MealType { get; init; }

    /// <summary>就餐方式；<see cref="DiningMode.Whatever"/> 表示不限。</summary>
    public DiningMode DiningMode { get; init; } = DiningMode.Whatever;

    /// <summary>用餐人数。</summary>
    public short PartySize { get; init; } = 1;

    /// <summary>本次预算下限（分）；null 时用画像值。</summary>
    public int? BudgetMinCents { get; init; }

    /// <summary>本次预算上限（分）；null 时用画像值。</summary>
    public int? BudgetMaxCents { get; init; }

    /// <summary>当前位置；null 表示无定位。</summary>
    public GeoPoint? Location { get; init; }

    /// <summary>天气：<c>clear</c> / <c>rain</c> / <c>hot</c> / <c>cold</c> / <c>snow</c>。</summary>
    public string? Weather { get; init; }

    /// <summary>快捷需求标签，最多 3 个。见 <see cref="MoodTags"/>。</summary>
    public IReadOnlyList<string> MoodTags { get; init; } = [];

    /// <summary>要排除的菜品（「换一批」时传入上一批）。</summary>
    public IReadOnlyList<Guid> ExcludeDishIds { get; init; } = [];

    /// <summary>分数抖动标准差（分）。0 = 完全确定性。</summary>
    public double ExploreJitter { get; init; }

    /// <summary>当前时间（UTC）。由调用方注入，保证引擎可确定性测试。</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>用户本地时区相对 UTC 的偏移，用于推断餐次与季节。</summary>
    public TimeSpan LocalOffset { get; init; } = TimeSpan.FromHours(8);

    /// <summary>解析出实际使用的餐次。</summary>
    public MealType ResolveMealType()
        => MealType ?? MealTimes.InferFromUtc(Now, LocalOffset);

    /// <summary>解析出用户本地时间。</summary>
    public DateTimeOffset LocalNow => Now.ToOffset(LocalOffset);

    /// <summary>解析出当前季节。</summary>
    public SeasonMask CurrentSeason => LocalNow.Month switch
    {
        3 or 4 or 5 => SeasonMask.Spring,
        6 or 7 or 8 => SeasonMask.Summer,
        9 or 10 or 11 => SeasonMask.Autumn,
        _ => SeasonMask.Winter,
    };
}
