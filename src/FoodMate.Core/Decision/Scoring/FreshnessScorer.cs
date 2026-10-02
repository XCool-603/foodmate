namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 新鲜度（<c>S_fresh</c>，默认权重 0.20）。
/// </summary>
/// <remarks>
/// <para>核心思想：<b>刚吃过的菜要抑制，很久没吃的菜要恢复，吃过太多次的菜要额外抑制。</b></para>
/// <code>
/// daysSince       = (Now - lastEatenAt).TotalDays
/// recencyFactor   = 1 - exp(-daysSince / τ)
/// frequencyFactor = 1 / (1 + α · 近30天次数)
/// S               = recencyFactor^0.6 · frequencyFactor^0.4   // 几何平均
/// </code>
/// <para>
/// 菜品级与菜系级合成：<c>S_fresh = 0.7·S_dish + 0.3·S_cuisine</c>。
/// 菜系级用更宽的 τ，避免惩罚过重。
/// </para>
/// <para>
/// <b>为什么新鲜度(0.20) 高于偏好强度(0.15)？</b>
/// 因为这是决策助手而非「猜你喜欢」。若偏好强度更高，产品会退化成
/// 永远推荐同一道菜，用户很快会腻。
/// </para>
/// </remarks>
public sealed class FreshnessScorer : IScorer
{
    /// <inheritdoc />
    public ScoreDimension Dimension => ScoreDimension.Freshness;

    /// <inheritdoc />
    public double Score(DishCandidate dish, DecisionContext context)
    {
        var options = context.Options.Freshness;
        var now = context.Request.Now;

        var dishStat = context.StatOf(dish.Id);
        var cuisineStat = context.CuisineStatOf(dish.Cuisine);

        var dishFresh = Compute(
            dishStat.LastEatenAt, dishStat.EatCount30d, now,
            options.DishTauDays, options);

        var cuisineFresh = Compute(
            cuisineStat.LastEatenAt, cuisineStat.EatCount30d, now,
            options.CuisineTauDays, options);

        return options.DishWeight * dishFresh + options.CuisineWeight * cuisineFresh;
    }

    /// <summary>
    /// 单个维度的新鲜度计算。公开以便单元测试直接验证公式。
    /// </summary>
    /// <param name="lastEatenAt">最近一次食用时间；null 表示从未吃过。</param>
    /// <param name="eatCount30d">近 30 天食用次数。</param>
    /// <param name="now">当前时间。</param>
    /// <param name="tauDays">时间衰减常数（天）。</param>
    /// <param name="options">新鲜度参数。</param>
    public static double Compute(
        DateTimeOffset? lastEatenAt,
        int eatCount30d,
        DateTimeOffset now,
        double tauDays,
        FreshnessOptions options)
    {
        double recency;

        if (lastEatenAt is null)
        {
            // 从未吃过 → 完全不抑制
            recency = 1.0;
        }
        else
        {
            var daysSince = Math.Max(0, (now - lastEatenAt.Value).TotalDays);
            recency = 1.0 - Math.Exp(-daysSince / tauDays);
        }

        var frequency = 1.0 / (1.0 + options.FrequencyAlpha * Math.Max(0, eatCount30d));

        return Math.Pow(recency, options.RecencyPower)
             * Math.Pow(frequency, options.FrequencyPower);
    }
}
