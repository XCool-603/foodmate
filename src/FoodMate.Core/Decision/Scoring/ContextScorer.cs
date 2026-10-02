using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 场景契合（<c>S_context</c>，默认权重 0.10）。
/// </summary>
/// <remarks>
/// <para>多因素加权，<b>缺失维度自动重新归一化</b>——不给缺失项打低分。</para>
/// <list type="bullet">
///   <item>就餐方式（w=0.4）</item>
///   <item>距离（w=0.3，仅堂食/外卖且有定位时参与）</item>
///   <item>天气（w=0.3，仅有天气信息时参与）</item>
///   <item>人数（w=0.2）</item>
/// </list>
/// <para><c>S_context = Σ(score · weight) / Σ(weight)</c></para>
/// </remarks>
public sealed class ContextScorer : IScorer
{
    private const double DiningModeWeight = 0.4;
    private const double DistanceWeight = 0.3;
    private const double WeatherWeight = 0.3;
    private const double PartySizeWeight = 0.2;

    /// <inheritdoc />
    public ScoreDimension Dimension => ScoreDimension.Context;

    /// <inheritdoc />
    public double Score(DishCandidate dish, DecisionContext context)
    {
        var request = context.Request;

        var weighted = 0.0;
        var totalWeight = 0.0;

        void Add(double score, double weight)
        {
            weighted += Math.Clamp(score, 0, 1) * weight;
            totalWeight += weight;
        }

        // ① 就餐方式
        if (request.DiningMode != DiningMode.Whatever)
        {
            Add(SupportsDiningMode(dish, request.DiningMode) ? 1.0 : 0.3, DiningModeWeight);
        }
        else
        {
            Add(context.Preference.DiningModeWeights.For(dish.PreferredDiningMode), DiningModeWeight);
        }

        // ② 距离：仅堂食/外卖且有定位时参与
        if (request.Location is not null
            && request.DiningMode is DiningMode.DineIn or DiningMode.Takeout)
        {
            if (dish.DistanceKm is not { } distanceKm)
            {
                Add(0.60, DistanceWeight);
            }
            else
            {
                var maxKm = Math.Max(0.1, context.Preference.MaxDistanceM / 1000.0);
                Add(Math.Clamp(1.0 - distanceKm / maxKm, 0, 1), DistanceWeight);
            }
        }

        // ③ 天气
        if (!string.IsNullOrWhiteSpace(request.Weather))
        {
            Add(WeatherScore(dish, request.Weather), WeatherWeight);
        }

        // ④ 人数（1 人也参与：一个人不该被推荐火锅）
        Add(PartySizeScore(dish, request.PartySize), PartySizeWeight);

        return totalWeight <= 0 ? 0.5 : weighted / totalWeight;
    }

    /// <summary>该菜品是否支持指定的就餐方式。</summary>
    public static bool SupportsDiningMode(DishCandidate dish, DiningMode mode) => mode switch
    {
        // 「自己做」必须有菜谱
        DiningMode.Homemade => dish.HasRecipe,
        DiningMode.Takeout or DiningMode.DineIn => true,
        _ => true,
    };

    /// <summary>天气契合分。基准 0.6，按天气调整后钳制到 <c>[0,1]</c>。</summary>
    public static double WeatherScore(DishCandidate dish, string weather)
    {
        var score = 0.6;

        switch (weather?.ToLowerInvariant())
        {
            case "rain":
                if (IsWarming(dish)) score += 0.2;
                if (dish.Tags.Contains("凉菜")) score -= 0.3;
                break;

            case "hot":
                if (dish.SpicyLevel <= 1
                    && dish.Category is DishCategory.Vegetable or DishCategory.Drink
                    || dish.Tags.Contains("清淡")
                    || dish.Tags.Contains("凉菜"))
                {
                    score += 0.2;
                }
                if (dish.SpicyLevel >= 4 || dish.Category == DishCategory.Hotpot) score -= 0.3;
                break;

            case "cold":
            case "snow":
                if (IsWarming(dish)) score += 0.3;
                if (dish.Tags.Contains("凉菜") || dish.Category == DishCategory.Drink) score -= 0.4;
                break;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>人数契合分。基准 0.6，按人数与菜品「份量属性」调整。</summary>
    public static double PartySizeScore(DishCandidate dish, short partySize)
    {
        var singleFriendly = IsSingleFriendly(dish);
        var feast = IsFeast(dish);
        var score = 0.6;

        switch (partySize)
        {
            case <= 1:
                if (singleFriendly) score += 0.3;
                if (feast) score -= 0.4;
                break;

            case 2:
                if (dish.Tags.Contains("家常")) score += 0.2;
                break;

            case <= 4:
                if (feast) score += 0.3;
                if (singleFriendly) score -= 0.2;
                break;

            default:
                if (feast) score += 0.4;
                if (singleFriendly) score -= 0.4;
                break;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>暖胃 / 热食类菜品。</summary>
    private static bool IsWarming(DishCandidate dish)
        => dish.Category == DishCategory.Soup
           || dish.Tags.Contains("暖胃")
           || dish.Tags.Contains("热食")
           || dish.Tags.Contains("炖菜");

    /// <summary>适合一个人吃。</summary>
    private static bool IsSingleFriendly(DishCandidate dish)
        => dish.Category is DishCategory.Snack or DishCategory.Breakfast or DishCategory.Staple
           || dish.Tags.Contains("快手")
           || dish.Tags.Contains("单人");

    /// <summary>聚餐属性（大份 / 硬菜）。</summary>
    private static bool IsFeast(DishCandidate dish)
        => dish.Category == DishCategory.Hotpot
           || dish.Tags.Contains("聚餐")
           || dish.Tags.Contains("宴客")
           || dish.Tags.Contains("大份");
}
