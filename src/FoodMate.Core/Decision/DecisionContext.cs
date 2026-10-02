using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision;

/// <summary>
/// 决策引擎的完整输入。由 Application 层组装后交给 <see cref="IDecisionEngine"/>。
/// </summary>
/// <remarks>
/// 引擎是<b>纯函数</b>：不注入 DbContext、不调网络、不用 <c>DateTime.Now</c>。
/// 相同输入必须产生相同输出（抖动为 0 时）。
/// </remarks>
public sealed record DecisionContext
{
    /// <summary>用户本次的显式意图。</summary>
    public required DecisionRequest Request { get; init; }

    /// <summary>用户口味画像。</summary>
    public UserPreferenceSnapshot Preference { get; init; } = UserPreferenceSnapshot.Default;

    /// <summary>候选菜品全集（尚未硬过滤）。</summary>
    public required IReadOnlyList<DishCandidate> Candidates { get; init; }

    /// <summary>用户 × 菜品历史。</summary>
    public IReadOnlyDictionary<Guid, DishStatSnapshot> DishStats { get; init; }
        = new Dictionary<Guid, DishStatSnapshot>();

    /// <summary>用户 × 菜系历史。</summary>
    public IReadOnlyDictionary<Cuisine, CuisineStatSnapshot> CuisineStats { get; init; }
        = new Dictionary<Cuisine, CuisineStatSnapshot>();

    /// <summary>用户历史记录总数，用于冷启动判断。</summary>
    public int UserRecordCount { get; init; }

    /// <summary>引擎参数。</summary>
    public EngineOptions Options { get; init; } = new();

    /// <summary>取某道菜的历史统计；无记录时返回空快照。</summary>
    public DishStatSnapshot StatOf(Guid dishId)
        => DishStats.TryGetValue(dishId, out var stat) ? stat : DishStatSnapshot.Empty;

    /// <summary>取某个菜系的历史统计；无记录时返回空快照。</summary>
    public CuisineStatSnapshot CuisineStatOf(Cuisine cuisine)
        => CuisineStats.TryGetValue(cuisine, out var stat) ? stat : CuisineStatSnapshot.Empty;

    /// <summary>解析后的实际预算区间。</summary>
    public (int Min, int Max) ResolveBudget() => (
        Request.BudgetMinCents ?? Preference.BudgetMinCents,
        Request.BudgetMaxCents ?? Preference.BudgetMaxCents);
}
