namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 预算匹配（<c>S_budget</c>，默认权重 0.10）。
/// </summary>
/// <remarks>
/// <code>
/// dishPrice = 价格区间中值；区间缺失时按分类默认价兜底
///
/// 无预算信息        → 0.5（中性）
/// budgetMin ≤ p ≤ budgetMax → 1.0
/// p &lt; budgetMin   → max(0.70, 1 - 0.30·(min-p)/min)    // 比预算便宜，轻微降分
/// p &gt; budgetMax   → max(0, 1 - 2·(p-max)/max)          // 超 50% 即归零
/// </code>
/// </remarks>
public sealed class BudgetScorer : IScorer
{
    /// <inheritdoc />
    public ScoreDimension Dimension => ScoreDimension.Budget;

    /// <inheritdoc />
    public double Score(DishCandidate dish, DecisionContext context)
    {
        var (min, max) = context.ResolveBudget();
        return Score(dish.ReferencePriceCents, min, max);
    }

    /// <summary>按参考价与预算区间计算分数。公开以便单元测试。</summary>
    public static double Score(int priceCents, int budgetMinCents, int budgetMaxCents)
    {
        // 用户未设预算 → 中性，不惩罚
        if (budgetMaxCents <= 0)
        {
            return 0.5;
        }

        if (priceCents >= budgetMinCents && priceCents <= budgetMaxCents)
        {
            return 1.0;
        }

        if (priceCents < budgetMinCents)
        {
            if (budgetMinCents <= 0)
            {
                return 1.0;
            }

            var ratio = (double)(budgetMinCents - priceCents) / budgetMinCents;
            return Math.Max(0.70, 1.0 - 0.30 * ratio);
        }

        // 超预算：按超出比例衰减，超 50% 归零
        var over = (double)(priceCents - budgetMaxCents) / budgetMaxCents;
        return Math.Max(0.0, 1.0 - 2.0 * over);
    }
}
