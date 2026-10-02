using System.Globalization;
using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision;

/// <summary>
/// 推荐理由生成器。
/// </summary>
/// <remarks>
/// <para>
/// <b>这是本产品相对竞品的核心差异。</b>用户不信任黑盒推荐——
/// 每个结果都必须能说出「为什么推荐它」。
/// </para>
/// <para>规则：</para>
/// <list type="number">
///   <item>取<b>贡献值最大</b>的维度（贡献 = 权重 × 归一化分）。</item>
///   <item>最多 2 条——超过 2 条用户不读。</item>
///   <item>至少 1 条；模板都不命中时用兜底文案。</item>
///   <item><b>不出现负面表述</b>。绝不写「因为你上周吃了太多辣」这种指责性文案。</item>
/// </list>
/// </remarks>
public sealed class ReasonGenerator
{
    private const int MaxReasons = 2;

    private const string FallbackReason = "今天的推荐";

    /// <summary>
    /// 理由优先级系数：在「贡献值」之上再乘一层信息量权重。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 单纯按贡献值排序会有个问题：<b>时段</b>几乎对每道候选菜都得满分
    /// （只要它适合当前餐次），于是「午餐吃这个正合适」这类<b>无信息量</b>的理由
    /// 会稳定挤掉「上次你给它打了 4.5 分」这类真正有说服力的理由。
    /// </para>
    /// <para>
    /// 因此给<b>个人化、具体</b>的维度加权，给<b>普适、必然命中</b>的维度降权。
    /// </para>
    /// </remarks>
    private static readonly Dictionary<ScoreDimension, double> ReasonPriority = new()
    {
        [ScoreDimension.Affinity] = 1.6,     // 个人历史最有说服力
        [ScoreDimension.Freshness] = 1.3,
        [ScoreDimension.Context] = 1.2,      // 天气 / 距离很具体
        [ScoreDimension.Exploration] = 1.1,
        [ScoreDimension.Taste] = 1.0,
        [ScoreDimension.Budget] = 0.7,       // 预算匹配很常见
        [ScoreDimension.TimeSlot] = 0.6,     // 几乎总是命中，信息量低
    };

    /// <summary>为打分结果生成 1–2 条人话理由。</summary>
    public IReadOnlyList<string> Generate(ScoredDish scored, DecisionContext context)
    {
        var weights = context.Options.Weights;

        // 按「权重 × 归一化分 × 信息量系数」降序尝试各维度模板
        var ordered = scored.Breakdown
            .OrderByDescending(pair => weights[pair.Key] * pair.Value * PriorityOf(pair.Key))
            .ThenBy(pair => pair.Key)
            .Select(pair => pair.Key);

        var reasons = new List<string>(MaxReasons);

        foreach (var dimension in ordered)
        {
            var text = Build(dimension, scored, context);

            if (!string.IsNullOrWhiteSpace(text))
            {
                reasons.Add(text);
            }

            if (reasons.Count >= MaxReasons)
            {
                break;
            }
        }

        if (reasons.Count == 0)
        {
            reasons.Add(FallbackReason);
        }

        return reasons;
    }

    private static double PriorityOf(ScoreDimension dimension)
        => ReasonPriority.TryGetValue(dimension, out var value) ? value : 1.0;

    private static string? Build(ScoreDimension dimension, ScoredDish scored, DecisionContext context)
    {
        var dish = scored.Dish;
        var stat = context.StatOf(dish.Id);

        return dimension switch
        {
            ScoreDimension.Taste => BuildTaste(scored, dish, context),
            ScoreDimension.Freshness => BuildFreshness(scored, dish, stat, context),
            ScoreDimension.Affinity => BuildAffinity(scored, stat),
            ScoreDimension.TimeSlot => BuildTimeSlot(scored, context),
            ScoreDimension.Budget => BuildBudget(scored, dish),
            ScoreDimension.Context => BuildContext(scored, dish, context),
            ScoreDimension.Exploration => BuildExploration(stat),
            _ => null,
        };
    }

    private static string? BuildTaste(ScoredDish scored, DishCandidate dish, DecisionContext context)
    {
        if (scored.ScoreOf(ScoreDimension.Taste) < 0.85)
        {
            return null;
        }

        // 辣度高度契合时优先说辣度（必须与用户偏好比较，而非菜品自身）
        var spicyFit = Scoring.TasteScorer.ScoreSpicy(
            dish.SpicyLevel, context.Preference.SpicyLevel);

        if (spicyFit >= 0.95 && dish.SpicyLevel > 0)
        {
            return $"{EnumLabels.SpicyLevel(dish.SpicyLevel)}，正合你口味";
        }

        if (context.Preference.PreferredCuisines.Contains(dish.Cuisine)
            && dish.Cuisine != Cuisine.Other)
        {
            return $"你爱吃{EnumLabels.Cuisine(dish.Cuisine)}";
        }

        return "这道挺对你胃口";
    }

    private static string? BuildFreshness(
        ScoredDish scored,
        DishCandidate dish,
        DishStatSnapshot stat,
        DecisionContext context)
    {
        var freshness = scored.ScoreOf(ScoreDimension.Freshness);

        if (freshness < 0.85)
        {
            return null;
        }

        // 没吃过的菜 + 该菜系最近吃得不少 → 换个新花样
        var cuisineRecent = context.CuisineStatOf(dish.Cuisine).EatCount30d;
        if (stat.EatCount == 0 && cuisineRecent >= 3 && dish.Cuisine != Cuisine.Other)
        {
            return $"最近{EnumLabels.Cuisine(dish.Cuisine)}吃得有点多，这道你还没试过";
        }

        return stat.EatCount > 0 ? "有阵子没吃这道了" : null;
    }

    private static string? BuildAffinity(ScoredDish scored, DishStatSnapshot stat)
    {
        if (scored.ScoreOf(ScoreDimension.Affinity) < 0.6)
        {
            return null;
        }

        if (stat.AverageRating is { } average && average >= 4.0)
        {
            return $"上次你给它打了 {Format(average)} 分";
        }

        if (stat.EatCount >= 3)
        {
            return $"你吃过 {stat.EatCount} 次，是老朋友了";
        }

        return null;
    }

    private static string? BuildTimeSlot(ScoredDish scored, DecisionContext context)
    {
        if (scored.ScoreOf(ScoreDimension.TimeSlot) < 0.99)
        {
            return null;
        }

        var mealType = context.Request.ResolveMealType();
        return $"{MealTimes.Label(mealType)}吃这个正合适";
    }

    private static string? BuildBudget(ScoredDish scored, DishCandidate dish)
    {
        if (scored.ScoreOf(ScoreDimension.Budget) < 0.99)
        {
            return null;
        }

        return $"¥{Format(dish.ReferencePriceCents / 100.0)}，在你的预算内";
    }

    private static string? BuildContext(ScoredDish scored, DishCandidate dish, DecisionContext context)
    {
        if (scored.ScoreOf(ScoreDimension.Context) < 0.7)
        {
            return null;
        }

        var weather = context.Request.Weather?.ToLowerInvariant();

        var warming = dish.Category == DishCategory.Soup
                      || dish.Tags.Contains("暖胃")
                      || dish.Tags.Contains("热食")
                      || dish.Tags.Contains("炖菜");

        switch (weather)
        {
            case "rain" when warming:
                return "下雨天，来点热乎的";

            case "cold" or "snow" when warming:
                return "天冷，来点暖胃的";

            case "hot" when dish.SpicyLevel <= 1
                            && (dish.Category == DishCategory.Vegetable
                                || dish.Tags.Contains("清淡")
                                || dish.Tags.Contains("凉菜")):
                return "天热，来点清爽的";
        }

        if (dish.DistanceKm is { } km and <= 1.0)
        {
            return $"就在附近，{Math.Round(km * 1000 / 50) * 50:0} 米";
        }

        if (context.Request.PartySize > 1 && dish.Category == DishCategory.Hotpot)
        {
            return "人多热闹，正适合一起吃";
        }

        return null;
    }

    private static string? BuildExploration(DishStatSnapshot stat)
        => stat.EatCount == 0 ? "你没试过这道，要不要尝尝" : null;

    private static string Format(double value)
        => value.ToString("0.#", CultureInfo.InvariantCulture);
}
