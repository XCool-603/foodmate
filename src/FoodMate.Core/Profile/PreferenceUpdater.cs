using FoodMate.Core.Entities;
using FoodMate.Core.Enums;

namespace FoodMate.Core.Profile;

/// <summary>
/// 从饮食记录中提炼出的、用于学习画像的单条事实。
/// </summary>
/// <param name="Cuisine">菜系。</param>
/// <param name="SpicyLevel">辣度 0–5。</param>
/// <param name="PriceCents">该顿的参考价（分）；未知为 null。</param>
/// <param name="DiningMode">就餐方式。</param>
/// <param name="Rating">评分 1–5；未评为 null。</param>
/// <param name="EatenAt">就餐时间。</param>
public sealed record LearnedRecordFact(
    Cuisine Cuisine,
    short SpicyLevel,
    int? PriceCents,
    DiningMode DiningMode,
    short? Rating,
    DateTimeOffset EatenAt);

/// <summary>
/// 学习得到的画像建议。
/// </summary>
/// <remarks>
/// 每个字段独立可空：样本只够支撑某一项时，其余项保持 <c>null</c>，
/// 调用方只应用非空字段。
/// </remarks>
public sealed record PreferenceSuggestion
{
    /// <summary>建议的辣度偏好。</summary>
    public short? SpicyLevel { get; init; }

    /// <summary>建议的预算下限（分）。</summary>
    public int? BudgetMinCents { get; init; }

    /// <summary>建议的预算上限（分）。</summary>
    public int? BudgetMaxCents { get; init; }

    /// <summary>建议的偏好菜系。</summary>
    public IReadOnlyList<Cuisine>? PreferredCuisines { get; init; }

    /// <summary>建议的就餐方式权重。</summary>
    public DiningModeWeights? DiningModeWeights { get; init; }

    /// <summary>参与学习的样本量。</summary>
    public int SampleSize { get; init; }

    /// <summary>是否给出了任何建议。</summary>
    public bool HasAny =>
        SpicyLevel is not null
        || BudgetMinCents is not null
        || PreferredCuisines is not null
        || DiningModeWeights is not null;
}

/// <summary>画像学习的可调参数。</summary>
public sealed class PreferenceLearningOptions
{
    /// <summary>低于此样本量不做任何推断。</summary>
    public int MinSampleSize { get; set; } = 10;

    /// <summary>辣度学习的时间窗口（天）。</summary>
    public int SpicyWindowDays { get; set; } = 30;

    /// <summary>预算学习的取样条数。</summary>
    public int BudgetSampleSize { get; set; } = 20;

    /// <summary>菜系学习的时间窗口（天）。</summary>
    public int CuisineWindowDays { get; set; } = 60;

    /// <summary>菜系建议取前几名。</summary>
    public int CuisineTopN { get; set; } = 3;

    /// <summary>某菜系至少出现几次才纳入建议。</summary>
    public int MinCuisineCount { get; set; } = 2;
}

/// <summary>
/// 从饮食记录反推口味画像。
/// </summary>
/// <remarks>
/// <para>
/// <b>纯函数</b>：输入历史事实，输出建议，不碰数据库、不读时钟。
/// </para>
/// <para>
/// <b>设计取舍：学习但不静默覆盖。</b>
/// 用户在手填的画像里表达的是<b>意图</b>（「我想吃清淡点」），
/// 而历史记录反映的是<b>行为</b>（「最近总在吃川菜」）。
/// 两者不一致时——比如用户在减脂却总被同事拉去吃火锅——
/// 静默覆盖会抹掉用户的意图。因此本类只<b>产出建议</b>，
/// 由用户在画像页显式确认后才写入。
/// </para>
/// <para>
/// 唯一的例外是 <see cref="PreferenceSuggestion.DiningModeWeights"/>：
/// 该字段用户在界面上不直接编辑，纯属派生数据，可直接采用。
/// </para>
/// </remarks>
public static class PreferenceUpdater
{
    /// <summary>根据历史记录给出画像建议；样本不足时返回 <c>null</c>。</summary>
    public static PreferenceSuggestion? Suggest(
        IReadOnlyList<LearnedRecordFact> history,
        DateTimeOffset now,
        PreferenceLearningOptions? options = null)
    {
        options ??= new PreferenceLearningOptions();

        if (history.Count < options.MinSampleSize)
        {
            return null;
        }

        var (budgetMin, budgetMax) = SuggestBudget(history, options);

        return new PreferenceSuggestion
        {
            SpicyLevel = SuggestSpicyLevel(history, now, options),
            BudgetMinCents = budgetMin,
            BudgetMaxCents = budgetMax,
            PreferredCuisines = SuggestCuisines(history, now, options),
            DiningModeWeights = SuggestDiningModeWeights(history),
            SampleSize = history.Count,
        };
    }

    /// <summary>
    /// 辣度：取最近 <c>SpicyWindowDays</c> 天记录的<b>加权中位数</b>。
    /// </summary>
    /// <remarks>
    /// 用中位数而非均值：一次「变态辣」的猎奇体验不该把偏好拉高一大截。
    /// 评分高的记录权重更大——「爱吃的菜」更能代表真实口味。
    /// </remarks>
    public static short? SuggestSpicyLevel(
        IReadOnlyList<LearnedRecordFact> history,
        DateTimeOffset now,
        PreferenceLearningOptions options)
    {
        var since = now.AddDays(-options.SpicyWindowDays);

        var samples = history
            .Where(f => f.EatenAt >= since)
            .Select(f => (Value: (double)f.SpicyLevel, Weight: 1.0 + (f.Rating ?? 3) / 5.0))
            .OrderBy(s => s.Value)
            .ToList();

        if (samples.Count < 3)
        {
            return null;
        }

        var totalWeight = samples.Sum(s => s.Weight);
        var half = totalWeight / 2.0;
        var cumulative = 0.0;

        foreach (var sample in samples)
        {
            cumulative += sample.Weight;
            if (cumulative >= half)
            {
                return (short)Math.Round(sample.Value);
            }
        }

        return (short)Math.Round(samples[^1].Value);
    }

    /// <summary>
    /// 预算：取最近 <c>BudgetSampleSize</c> 条有价格的记录的 <b>25% / 75% 分位数</b>。
    /// </summary>
    /// <remarks>
    /// 用分位数而非最小/最大值：单次奢侈或单次凑合都不该定义用户的常规预算。
    /// </remarks>
    public static (int? Min, int? Max) SuggestBudget(
        IReadOnlyList<LearnedRecordFact> history,
        PreferenceLearningOptions options)
    {
        var prices = history
            .Where(f => f.PriceCents is > 0)
            .OrderByDescending(f => f.EatenAt)
            .Take(options.BudgetSampleSize)
            .Select(f => f.PriceCents!.Value)
            .OrderBy(p => p)
            .ToList();

        if (prices.Count < 5)
        {
            return (null, null);
        }

        var min = prices[(int)Math.Floor((prices.Count - 1) * 0.25)];
        var max = prices[(int)Math.Ceiling((prices.Count - 1) * 0.75)];

        return (RoundToHundred(min), RoundToHundred(max));
    }

    /// <summary>
    /// 偏好菜系：最近 <c>CuisineWindowDays</c> 天内出现次数最多的前几名。
    /// </summary>
    public static IReadOnlyList<Cuisine>? SuggestCuisines(
        IReadOnlyList<LearnedRecordFact> history,
        DateTimeOffset now,
        PreferenceLearningOptions options)
    {
        var since = now.AddDays(-options.CuisineWindowDays);

        var top = history
            .Where(f => f.EatenAt >= since && f.Cuisine != Cuisine.Other)
            .GroupBy(f => f.Cuisine)
            .Select(g => new
            {
                Cuisine = g.Key,
                Count = g.Count(),
                // 评分高的记录额外加权，避免「被迫吃的工作餐」主导偏好
                Score = g.Count() + g.Count(f => (f.Rating ?? 0) >= 4) * 0.5,
            })
            .Where(x => x.Count >= options.MinCuisineCount)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Cuisine)
            .Take(options.CuisineTopN)
            .Select(x => x.Cuisine)
            .ToList();

        return top.Count > 0 ? top : null;
    }

    /// <summary>
    /// 就餐方式权重：历史占比。该项属派生数据，用户不直接编辑。
    /// </summary>
    public static DiningModeWeights? SuggestDiningModeWeights(
        IReadOnlyList<LearnedRecordFact> history)
    {
        var relevant = history
            .Where(f => f.DiningMode != DiningMode.Whatever)
            .ToList();

        if (relevant.Count < 5)
        {
            return null;
        }

        var total = (double)relevant.Count;

        return new DiningModeWeights
        {
            Takeout = relevant.Count(f => f.DiningMode == DiningMode.Takeout) / total,
            DineIn = relevant.Count(f => f.DiningMode == DiningMode.DineIn) / total,
            Homemade = relevant.Count(f => f.DiningMode == DiningMode.Homemade) / total,
        };
    }

    /// <summary>把金额取整到「元」，避免给出 ¥37.3 这种不体面的预算边界。</summary>
    private static int RoundToHundred(int cents) => (int)Math.Round(cents / 100.0) * 100;
}
