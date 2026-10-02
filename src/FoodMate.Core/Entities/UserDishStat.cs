namespace FoodMate.Core.Entities;

/// <summary>
/// 用户 × 菜品统计。决策引擎 <c>S_fresh</c> 与 <c>S_affinity</c> 的数据源。
/// </summary>
/// <remarks>
/// <b>派生数据</b>：任何时候可从 <see cref="MealRecord"/> 全量重算。
/// 记录增删改时在同一事务内增量维护。
/// </remarks>
public sealed class UserDishStat
{
    public Guid UserId { get; set; }

    public Guid DishId { get; set; }

    /// <summary>累计食用次数（按份量加权取整）。</summary>
    public int EatCount { get; set; }

    /// <summary>最近一次食用时间。新鲜度计算的核心。</summary>
    public DateTimeOffset? LastEatenAt { get; set; }

    public int RatingSum { get; set; }

    public int RatingCount { get; set; }

    public int WouldEatAgainCount { get; set; }

    /// <summary>明确标记「不想再吃」的次数。与 <see cref="WouldEatAgainCount"/> 配合判断 NEVER_AGAIN 惩罚。</summary>
    public int WouldNotEatAgainCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>平均评分；无评分时为 null。</summary>
    public double? AverageRating => RatingCount > 0 ? (double)RatingSum / RatingCount : null;

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }

    public Dish? Dish { get; set; }
}
