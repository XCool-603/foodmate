using System.Diagnostics;
using FoodMate.Core.Decision.Scoring;

namespace FoodMate.Core.Decision;

/// <summary>
/// 加权打分决策引擎。
/// </summary>
/// <remarks>
/// <para>流程：</para>
/// <list type="number">
///   <item><b>硬过滤</b>：移除下架、他人私有、含忌口/过敏原、不满足就餐方式的候选。</item>
///   <item><b>保底降级</b>：候选不足时放宽就餐方式约束（<b>忌口永不放开</b>）。</item>
///   <item><b>七维打分</b>：<c>TotalScore = Σ(wᵢ · Sᵢ)</c>。</item>
///   <item><b>乘性惩罚</b>：<c>FinalScore = TotalScore × Π Pⱼ</c>。</item>
///   <item><b>排除 + 抖动 + 排序</b>。</item>
///   <item><b>生成理由</b>：取贡献最大的 2 个维度套用文案模板。</item>
/// </list>
/// <para>
/// Scorer 通过 DI 注入，新增维度无需改动本类。
/// </para>
/// </remarks>
public sealed class DecisionEngine : IDecisionEngine
{
    private readonly IReadOnlyList<IScorer> _scorers;
    private readonly PenaltyEvaluator _penalties;
    private readonly ReasonGenerator _reasons;
    private readonly IRandomSource _random;

    /// <summary>构造引擎。</summary>
    /// <param name="scorers">各维度打分器。</param>
    /// <param name="penalties">惩罚评估器。</param>
    /// <param name="reasons">理由生成器。</param>
    /// <param name="random">随机源；抖动为 0 时不会被使用。</param>
    public DecisionEngine(
        IEnumerable<IScorer> scorers,
        PenaltyEvaluator penalties,
        ReasonGenerator reasons,
        IRandomSource random)
    {
        _scorers = [.. scorers];
        _penalties = penalties;
        _reasons = reasons;
        _random = random;

        if (_scorers.Count == 0)
        {
            throw new ArgumentException("至少需要注册一个 Scorer。", nameof(scorers));
        }
    }

    /// <inheritdoc />
    public DecisionResult Decide(DecisionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var options = context.Options;
        options.Validate();

        // ── 阶段 1：硬过滤 ──────────────────────────────────
        var filtered = HardFilter.Apply(context);
        var filteredOut = context.Candidates.Count - filtered.Count;

        // ── 阶段 2：保底降级 ────────────────────────────────
        var (candidates, relaxedLevel) = EnsureMinimum(filtered, context, options.TopN.Wheel);

        // ── 阶段 3–4：打分 + 惩罚 ───────────────────────────
        var scored = new List<ScoredDish>(candidates.Count);

        foreach (var dish in candidates)
        {
            var breakdown = new Dictionary<ScoreDimension, double>(_scorers.Count);
            var total = 0.0;

            foreach (var scorer in _scorers)
            {
                var value = Math.Clamp(scorer.Score(dish, context), 0, 1);
                breakdown[scorer.Dimension] = value;
                total += options.Weights[scorer.Dimension] * value;
            }

            var penalties = _penalties.Evaluate(dish, context);
            var finalScore = total;

            foreach (var penalty in penalties)
            {
                finalScore *= penalty.Multiplier;
            }

            scored.Add(new ScoredDish
            {
                DishId = dish.Id,
                Name = dish.Name,
                Score = finalScore * 100.0,
                Breakdown = breakdown,
                Penalties = penalties,
                Dish = dish,
            });
        }

        // ── 阶段 5：排除 + 抖动 + 排序 ──────────────────────
        IEnumerable<ScoredDish> pool = scored;

        if (context.Request.ExcludeDishIds.Count > 0)
        {
            var excluded = new HashSet<Guid>(context.Request.ExcludeDishIds);
            pool = pool.Where(item => !excluded.Contains(item.DishId));
        }

        if (context.Request.ExploreJitter > 0)
        {
            var sigma = context.Request.ExploreJitter;
            pool = pool.Select(item => item with
            {
                Score = item.Score + _random.NextGaussian() * sigma,
            });
        }

        var ranked = pool
            .OrderByDescending(item => item.Score)
            // 打破平局：保证相同输入产生完全相同的顺序
            .ThenBy(item => item.DishId)
            .Take(options.TopN.ResultList)
            .ToList();

        // ── 阶段 6：生成理由 ────────────────────────────────
        var withReasons = new List<ScoredDish>(ranked.Count);
        foreach (var item in ranked)
        {
            withReasons.Add(item with { Reasons = _reasons.Generate(item, context) });
        }

        stopwatch.Stop();

        return new DecisionResult
        {
            Ranked = withReasons,
            CandidateCount = candidates.Count,
            FilteredOutCount = filteredOut,
            RelaxedLevel = relaxedLevel,
            EngineVersion = options.Version,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
        };
    }

    /// <summary>
    /// 候选不足时逐级放宽约束。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 设计文档原本规划了 4 级降级链（时段 → 距离 → 就餐方式 → 仅保留忌口）。
    /// 实施时发现<b>只有就餐方式是硬过滤</b>——时段与距离都被移到了打分环节，
    /// 因为它们属于「不太合适」而非「不能吃」。
    /// </para>
    /// <para>
    /// 因此降级链收缩为两级：先放宽就餐方式，仍不足则接受现状。
    /// <b>忌口与过敏原在任何层级都不放开。</b>
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<DishCandidate> Candidates, int Level) EnsureMinimum(
        IReadOnlyList<DishCandidate> strict,
        DecisionContext context,
        int minCount)
    {
        if (strict.Count >= minCount)
        {
            return (strict, 0);
        }

        var relaxed = HardFilter.Apply(context, enforceDiningMode: false);

        return relaxed.Count > strict.Count
            ? (relaxed, 1)
            : (strict, 0);
    }
}
