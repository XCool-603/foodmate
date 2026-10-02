namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 偏好强度（<c>S_affinity</c>，默认权重 0.15）。
/// </summary>
/// <remarks>
/// <code>
/// ratingScore       = 有评分 ? (avgRating - 1) / 4 : 0.5
/// freqScore         = 吃过 ? min(1, ln(1+eatCount) / ln(1+FreqCap)) : 0.3
/// wouldEatAgainRate = 吃过 ? againCount / eatCount : 0.5
///
/// S_affinity = 0.5·ratingScore + 0.3·freqScore + 0.2·wouldEatAgainRate
/// </code>
/// <para>
/// <b>冷启动兜底</b>：用户记录数低于阈值时，用全局热度（<c>popularity</c>）
/// 混合 50%，让新用户第一次用就能拿到合理结果。
/// </para>
/// </remarks>
public sealed class AffinityScorer : IScorer
{
    /// <inheritdoc />
    public ScoreDimension Dimension => ScoreDimension.Affinity;

    /// <inheritdoc />
    public double Score(DishCandidate dish, DecisionContext context)
    {
        var options = context.Options.Affinity;
        var stat = context.StatOf(dish.Id);

        double ratingScore;
        if (stat.RatingCount > 0 && stat.AverageRating is { } average)
        {
            // 1 分 → 0，5 分 → 1
            ratingScore = (average - 1.0) / 4.0;
        }
        else
        {
            ratingScore = options.DefaultRatingScore;
        }

        double freqScore;
        double againRate;

        if (stat.EatCount > 0)
        {
            freqScore = Math.Min(
                1.0,
                Math.Log(1 + stat.EatCount) / Math.Log(1 + options.FreqCap));

            againRate = stat.WouldEatAgainRate ?? options.DefaultAgainRate;
        }
        else
        {
            freqScore = options.DefaultFreqScore;
            againRate = options.DefaultAgainRate;
        }

        var affinity =
            options.RatingWeight * ratingScore
            + options.FrequencyWeight * freqScore
            + options.AgainWeight * againRate;

        // 冷启动：历史太少，用全局热度兜底
        var coldStart = context.Options.ColdStart;
        if (context.UserRecordCount < coldStart.MinRecordsForAffinity)
        {
            var popularity = Math.Clamp(dish.Popularity / 100.0, 0, 1);
            var blend = Math.Clamp(coldStart.PopularityBlend, 0, 1);

            affinity = (1 - blend) * affinity + blend * popularity;
        }

        return affinity;
    }
}
