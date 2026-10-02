namespace FoodMate.Core.Decision;

/// <summary>已应用的乘性惩罚。</summary>
/// <param name="Code">惩罚代号，如 <c>EATEN_24H</c>。</param>
/// <param name="Multiplier">乘数（0–1）。</param>
/// <param name="Description">面向用户的可读说明。</param>
public sealed record AppliedPenalty(string Code, double Multiplier, string Description);

/// <summary>单道菜的打分结果。</summary>
public sealed record ScoredDish
{
    /// <summary>菜品 ID。</summary>
    public required Guid DishId { get; init; }

    /// <summary>菜名。</summary>
    public required string Name { get; init; }

    /// <summary>最终得分 0–100。</summary>
    public required double Score { get; init; }

    /// <summary>各维度<b>归一化分</b>（0–1），非加权贡献。</summary>
    public required IReadOnlyDictionary<ScoreDimension, double> Breakdown { get; init; }

    /// <summary>已应用的乘性惩罚。</summary>
    public IReadOnlyList<AppliedPenalty> Penalties { get; init; } = [];

    /// <summary>人话推荐理由，1–2 条。</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>原始候选（便于上层投影为 DTO）。</summary>
    public required DishCandidate Dish { get; init; }

    /// <summary>取某维度的归一化分。</summary>
    public double ScoreOf(ScoreDimension dimension)
        => Breakdown.TryGetValue(dimension, out var value) ? value : 0;
}

/// <summary>决策引擎输出。</summary>
public sealed record DecisionResult
{
    /// <summary>按分数降序排列的候选（最多 <c>TopN.ResultList</c> 条）。</summary>
    public required IReadOnlyList<ScoredDish> Ranked { get; init; }

    /// <summary>硬过滤后的候选总数（诊断用）。</summary>
    public int CandidateCount { get; init; }

    /// <summary>被硬过滤掉的数量（诊断用）。</summary>
    public int FilteredOutCount { get; init; }

    /// <summary>降级层级：0 = 未降级；&gt;0 表示放宽了条件。</summary>
    public int RelaxedLevel { get; init; }

    /// <summary>引擎版本。</summary>
    public string EngineVersion { get; init; } = string.Empty;

    /// <summary>引擎耗时（毫秒）。</summary>
    public long ElapsedMs { get; init; }

    /// <summary>转盘候选（默认 Top 8），与 <see cref="Ranked"/> 前 N 项同序。</summary>
    public IReadOnlyList<ScoredDish> WheelPicks(int count)
        => [.. Ranked.Take(count)];

    /// <summary>空结果。</summary>
    public static DecisionResult Empty(string engineVersion, int candidateCount = 0) => new()
    {
        Ranked = [],
        CandidateCount = candidateCount,
        FilteredOutCount = 0,
        EngineVersion = engineVersion,
    };
}
