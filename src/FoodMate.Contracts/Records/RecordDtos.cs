namespace FoodMate.Contracts.Records;

/// <summary>创建饮食记录。</summary>
public sealed record CreateRecordRequest
{
    /// <summary>关联菜品 ID；纯手动输入时可为空，但此时 <see cref="DishName"/> 必填。</summary>
    public Guid? DishId { get; init; }

    /// <summary>菜名。未关联菜品库时必填。</summary>
    public string? DishName { get; init; }

    /// <summary>餐次：1 早餐 / 2 午餐 / 3 晚餐 / 4 夜宵。</summary>
    public short MealType { get; init; }

    /// <summary>就餐方式：0 随便 / 1 外卖 / 2 堂食 / 3 自己做。</summary>
    public short DiningMode { get; init; }

    /// <summary>就餐时间；不传则取服务端当前时间。</summary>
    public DateTimeOffset? EatenAt { get; init; }

    /// <summary>份量系数，0.5 = 半份。</summary>
    public double Servings { get; init; } = 1.0;

    /// <summary>评分 1–5；可后补。</summary>
    public short? Rating { get; init; }

    /// <summary>还想再吃吗。</summary>
    public bool? WouldEatAgain { get; init; }

    /// <summary>照片地址。</summary>
    public string? PhotoUrl { get; init; }

    /// <summary>备注，≤ 200 字。</summary>
    public string? Note { get; init; }

    /// <summary>来源：1 手动 / 2 AI 识别 / 3 决策推荐 / 4 菜品库。</summary>
    public short Source { get; init; } = 1;

    /// <summary>来自哪次决策会话。</summary>
    public Guid? DecisionSessionId { get; init; }

    /// <summary>
    /// 忌口冲突时是否强制写入。
    /// </summary>
    /// <remarks>
    /// 记录是「我吃了什么」的客观事实，不该因为忌口被阻止。
    /// 服务端先返回 <c>2003</c> 让前端确认一次，用户确认后带 <c>force = true</c> 重试。
    /// </remarks>
    public bool Force { get; init; }
}

/// <summary>更新饮食记录（字段可选，只传要改的）。</summary>
public sealed record UpdateRecordRequest
{
    /// <summary>餐次。</summary>
    public short? MealType { get; init; }

    /// <summary>就餐方式。</summary>
    public short? DiningMode { get; init; }

    /// <summary>就餐时间。</summary>
    public DateTimeOffset? EatenAt { get; init; }

    /// <summary>份量系数。</summary>
    public double? Servings { get; init; }

    /// <summary>评分。</summary>
    public short? Rating { get; init; }

    /// <summary>还想再吃吗。</summary>
    public bool? WouldEatAgain { get; init; }

    /// <summary>备注。</summary>
    public string? Note { get; init; }

    /// <summary>照片地址。</summary>
    public string? PhotoUrl { get; init; }
}

/// <summary>快速评分。</summary>
public sealed record RateRecordRequest
{
    /// <summary>评分 1–5。</summary>
    public short Rating { get; init; }

    /// <summary>还想再吃吗。</summary>
    public bool? WouldEatAgain { get; init; }
}

/// <summary>记录中的菜品快照。</summary>
public sealed record DishSnapshotDto
{
    /// <summary>菜系。</summary>
    public short Cuisine { get; init; }

    /// <summary>菜系名称。</summary>
    public string CuisineLabel { get; init; } = string.Empty;

    /// <summary>分类。</summary>
    public short Category { get; init; }

    /// <summary>分类名称。</summary>
    public string CategoryLabel { get; init; } = string.Empty;

    /// <summary>辣度。</summary>
    public short SpicyLevel { get; init; }

    /// <summary>每份热量（kcal）。</summary>
    public int? CaloriesPerServing { get; init; }

    /// <summary>参考价（分）。</summary>
    public int? PriceCents { get; init; }

    /// <summary>标签。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>饮食记录。</summary>
public sealed record RecordDto
{
    /// <summary>记录 ID。</summary>
    public Guid Id { get; init; }

    /// <summary>关联菜品 ID。</summary>
    public Guid? DishId { get; init; }

    /// <summary>菜名（快照）。</summary>
    public string DishName { get; init; } = string.Empty;

    /// <summary>菜品属性快照。</summary>
    public DishSnapshotDto DishSnapshot { get; init; } = new();

    /// <summary>餐次。</summary>
    public short MealType { get; init; }

    /// <summary>餐次名称。</summary>
    public string MealTypeLabel { get; init; } = string.Empty;

    /// <summary>就餐方式。</summary>
    public short DiningMode { get; init; }

    /// <summary>就餐方式名称。</summary>
    public string DiningModeLabel { get; init; } = string.Empty;

    /// <summary>就餐时间。</summary>
    public DateTimeOffset EatenAt { get; init; }

    /// <summary>份量系数。</summary>
    public double Servings { get; init; }

    /// <summary>估算热量（已乘份量）。</summary>
    public int? Calories { get; init; }

    /// <summary>评分。</summary>
    public short? Rating { get; init; }

    /// <summary>还想再吃吗。</summary>
    public bool? WouldEatAgain { get; init; }

    /// <summary>照片。</summary>
    public string? PhotoUrl { get; init; }

    /// <summary>备注。</summary>
    public string? Note { get; init; }

    /// <summary>来源。</summary>
    public short Source { get; init; }

    /// <summary>来源名称。</summary>
    public string SourceLabel { get; init; } = string.Empty;
}

/// <summary>记录查询条件。</summary>
public sealed record RecordQuery
{
    /// <summary>页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;

    /// <summary>每页条数。</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>起始日期（含）。</summary>
    public DateOnly? From { get; init; }

    /// <summary>结束日期（含）。</summary>
    public DateOnly? To { get; init; }

    /// <summary>按餐次过滤。</summary>
    public short? MealType { get; init; }

    /// <summary>按菜品过滤。</summary>
    public Guid? DishId { get; init; }

    /// <summary>只看未评分（true）/ 只看已评分（false）。</summary>
    public bool? HasRating { get; init; }
}

/// <summary>分布项。</summary>
/// <param name="Value">枚举值。</param>
/// <param name="Label">显示名。</param>
/// <param name="Count">次数。</param>
public sealed record DistributionItemDto(short Value, string Label, int Count);

/// <summary>单日热量。</summary>
/// <param name="Date">日期。</param>
/// <param name="Calories">总热量。</param>
public sealed record DailyCaloriesDto(DateOnly Date, int Calories);

/// <summary>记录统计报表。</summary>
public sealed record RecordStatsDto
{
    /// <summary>统计区间。</summary>
    public DateOnly From { get; init; }

    /// <summary>统计区间结束。</summary>
    public DateOnly To { get; init; }

    /// <summary>记录总条数。</summary>
    public int TotalRecords { get; init; }

    /// <summary>总热量。</summary>
    public int TotalCalories { get; init; }

    /// <summary>平均每餐热量。</summary>
    public int AverageCaloriesPerMeal { get; init; }

    /// <summary>平均评分；无评分为 null。</summary>
    public double? AverageRating { get; init; }

    /// <summary>有记录的天数。</summary>
    public int RecordedDays { get; init; }

    /// <summary>尝试过的新菜品数（该区间内首次出现的菜）。</summary>
    public int NewDishesTried { get; init; }

    /// <summary>餐次分布。</summary>
    public IReadOnlyList<DistributionItemDto> MealTypeDistribution { get; init; } = [];

    /// <summary>菜系分布。</summary>
    public IReadOnlyList<DistributionItemDto> CuisineDistribution { get; init; } = [];

    /// <summary>就餐方式分布。</summary>
    public IReadOnlyList<DistributionItemDto> DiningModeDistribution { get; init; } = [];

    /// <summary>逐日热量。</summary>
    public IReadOnlyList<DailyCaloriesDto> DailyCalories { get; init; } = [];
}

/// <summary>待评分提醒项。</summary>
public sealed record PendingRatingDto
{
    /// <summary>记录 ID。</summary>
    public Guid Id { get; init; }

    /// <summary>菜名。</summary>
    public string DishName { get; init; } = string.Empty;

    /// <summary>照片。</summary>
    public string? PhotoUrl { get; init; }

    /// <summary>就餐时间。</summary>
    public DateTimeOffset EatenAt { get; init; }

    /// <summary>几天前。</summary>
    public int DaysAgo { get; init; }
}

/// <summary>「我的」页数据卡片。</summary>
public sealed record ProfileSummaryDto
{
    /// <summary>累计记录条数。</summary>
    public int TotalRecords { get; init; }

    /// <summary>尝试过的菜品数。</summary>
    public int TotalDishesTried { get; init; }

    /// <summary>当前连续打卡天数。</summary>
    public int CurrentStreakDays { get; init; }

    /// <summary>最长连续打卡天数。</summary>
    public int LongestStreakDays { get; init; }

    /// <summary>本周记录数。</summary>
    public int ThisWeekRecords { get; init; }

    /// <summary>本周尝新菜品数。</summary>
    public int ThisWeekNewDishes { get; init; }

    /// <summary>最常吃的菜系。</summary>
    public DistributionItemDto? FavoriteCuisine { get; init; }

    /// <summary>平均评分。</summary>
    public double? AverageRating { get; init; }
}
