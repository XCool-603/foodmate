namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 探索度（<c>S_explore</c>，默认权重 0.05）。
/// </summary>
/// <remarks>
/// <code>
/// eatCount == 0  → 1.0    // 没试过，鼓励
/// eatCount ≤ 2   → 0.7
/// 否则           → 0.3    // 吃太多次了，给别的菜让位
/// </code>
/// 权重仅 0.05，作用温和——只保证「偶尔给新菜一点机会」，不会强行推冷门菜。
/// </remarks>
public sealed class ExplorationScorer : IScorer
{
    /// <inheritdoc />
    public ScoreDimension Dimension => ScoreDimension.Exploration;

    /// <inheritdoc />
    public double Score(DishCandidate dish, DecisionContext context)
    {
        var eatCount = context.StatOf(dish.Id).EatCount;

        return eatCount switch
        {
            0 => 1.0,
            <= 2 => 0.7,
            _ => 0.3,
        };
    }
}
