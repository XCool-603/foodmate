using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 用户口味画像（1:1 于 <see cref="User"/>）。
/// </summary>
/// <remarks>
/// <see cref="AvoidIngredients"/> 是<b>安全字段</b>：只由用户显式设置，
/// 绝不由算法推断，且在决策引擎中作为硬过滤条件而非扣分项。
/// </remarks>
public sealed class UserPreference
{
    public Guid UserId { get; set; }

    /// <summary>辣度偏好 0–5。0 = 完全不吃辣。</summary>
    public short SpicyLevel { get; set; } = 2;

    /// <summary>每餐预算下限（分）。</summary>
    public int BudgetMinCents { get; set; } = 1500;

    /// <summary>每餐预算上限（分）。</summary>
    public int BudgetMaxCents { get; set; } = 5000;

    /// <summary>忌口 / 过敏食材。硬过滤条件。</summary>
    public List<string> AvoidIngredients { get; set; } = [];

    /// <summary>偏好菜系。</summary>
    public List<Cuisine> PreferredCuisines { get; set; } = [];

    /// <summary>就餐方式偏好权重。</summary>
    public DiningModeWeights DiningModeWeights { get; set; } = new();

    /// <summary>可接受距离（米）。</summary>
    public int MaxDistanceM { get; set; } = 2000;

    /// <summary>是否已完成新用户偏好引导。</summary>
    public bool OnboardingCompleted { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }
}
