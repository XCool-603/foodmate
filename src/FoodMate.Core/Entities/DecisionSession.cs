using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 决策会话。记录每次推荐的完整上下文与结果。
/// </summary>
public sealed class DecisionSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public MealType MealType { get; set; }

    public DiningMode DiningMode { get; set; }

    public short PartySize { get; set; } = 1;

    public int? BudgetMinCents { get; set; }

    public int? BudgetMaxCents { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    /// <summary>天气：clear / rain / hot / cold / snow。</summary>
    public string? Weather { get; set; }

    /// <summary>用户勾选的快捷需求标签。</summary>
    public List<string> MoodTags { get; set; } = [];

    /// <summary>硬过滤后的候选集大小（诊断用）。</summary>
    public int CandidateCount { get; set; }

    /// <summary>Top N 打分明细。</summary>
    public List<DecisionCandidateSnapshot> Candidates { get; set; } = [];

    /// <summary>用户最终选择了哪道菜；null 表示没选。</summary>
    public Guid? ChosenDishId { get; set; }

    public DateTimeOffset? ChosenAt { get; set; }

    /// <summary>引擎版本，便于 A/B 与回溯。</summary>
    public string EngineVersion { get; set; } = string.Empty;

    public int ElapsedMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }
}
