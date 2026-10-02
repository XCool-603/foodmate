namespace FoodMate.Core.Entities;

/// <summary>
/// 单次决策中被推荐菜品的打分明细快照（序列化为 <c>decision_sessions.candidates</c> 的元素）。
/// </summary>
/// <remarks>
/// 这张数据是权重调优的金矿：记录了「推荐了什么、给了多少分、为什么」，
/// 与 <see cref="DecisionSession.ChosenDishId"/> 结合即可分析采纳率与排名偏移。
/// </remarks>
public sealed class DecisionCandidateSnapshot
{
    public Guid DishId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>总分 0–100。</summary>
    public double TotalScore { get; set; }

    /// <summary>各维度归一化分（0–1），键为维度名。</summary>
    public Dictionary<string, double> Breakdown { get; set; } = [];

    /// <summary>已应用的乘性惩罚代号。</summary>
    public List<string> Penalties { get; set; } = [];

    public List<string> Reasons { get; set; } = [];
}
