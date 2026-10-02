namespace FoodMate.Core.Decision;

/// <summary>
/// 乘性惩罚：在加权总分之上再做衰减。
/// </summary>
/// <remarks>
/// <para>
/// 惩罚用<b>乘法</b>而非减法，因为它们表达的是「这几乎不可选」这类强信号——
/// 多个惩罚叠加后会迅速趋近于 0，符合直觉。
/// </para>
/// <table>
///   <tr><th>代号</th><th>触发条件</th><th>默认乘数</th></tr>
///   <tr><td>EATEN_24H</td><td>24 小时内吃过</td><td>0.10</td></tr>
///   <tr><td>DISLIKED</td><td>平均评分 ≤ 2.0</td><td>0.25</td></tr>
///   <tr><td>MEH</td><td>平均评分 ≤ 3.0</td><td>0.70</td></tr>
///   <tr><td>NEVER_AGAIN</td><td>明确表示不想再吃</td><td>0.30</td></tr>
/// </table>
/// <para><c>DISLIKED</c> 与 <c>MEH</c> 互斥，不叠加。</para>
/// </remarks>
public sealed class PenaltyEvaluator
{
    /// <summary>评估某道菜应应用的惩罚。</summary>
    public IReadOnlyList<AppliedPenalty> Evaluate(DishCandidate dish, DecisionContext context)
    {
        var options = context.Options.Penalties;
        var stat = context.StatOf(dish.Id);
        var now = context.Request.Now;

        var penalties = new List<AppliedPenalty>(2);

        // 24 小时内刚吃过
        if (stat.LastEatenAt is { } lastEaten
            && now - lastEaten < options.RecentWindow
            && now >= lastEaten)
        {
            penalties.Add(new AppliedPenalty(
                "EATEN_24H", options.EatenWithin24h, "刚吃过没多久"));
        }

        // 评分反馈（差评与一般般互斥）
        if (stat.RatingCount > 0 && stat.AverageRating is { } average)
        {
            if (average <= options.DislikedThreshold)
            {
                penalties.Add(new AppliedPenalty(
                    "DISLIKED", options.Disliked, "你之前给它的评分偏低"));
            }
            else if (average <= options.MehThreshold)
            {
                penalties.Add(new AppliedPenalty(
                    "MEH", options.Meh, "上次评分一般"));
            }
        }

        // 明确表示不想再吃
        if (stat.IsNeverAgain)
        {
            penalties.Add(new AppliedPenalty(
                "NEVER_AGAIN", options.NeverAgain, "你说过不想再吃"));
        }

        return penalties;
    }
}
