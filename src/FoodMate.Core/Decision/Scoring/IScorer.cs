namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 单个打分维度。
/// </summary>
/// <remarks>
/// 通过 DI 注册，<see cref="DecisionEngine"/> 只依赖 <c>IEnumerable&lt;IScorer&gt;</c>，
/// <b>新增维度无需改动引擎代码</b>。
/// </remarks>
public interface IScorer
{
    /// <summary>本 Scorer 负责的维度。</summary>
    ScoreDimension Dimension { get; }

    /// <summary>计算归一化分数，返回值会被引擎钳制到 <c>[0, 1]</c>。</summary>
    double Score(DishCandidate dish, DecisionContext context);
}
