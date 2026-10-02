namespace FoodMate.Core.Decision;

/// <summary>
/// 决策引擎。
/// </summary>
/// <remarks>
/// <b>纯函数</b>：不注入 DbContext、不调网络、不用 <c>DateTime.Now</c>。
/// 相同输入（抖动为 0 时）必须产生完全相同的输出。
/// </remarks>
public interface IDecisionEngine
{
    /// <summary>执行决策。</summary>
    DecisionResult Decide(DecisionContext context);
}
