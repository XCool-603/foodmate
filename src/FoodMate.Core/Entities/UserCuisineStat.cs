using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 用户 × 菜系统计。用于决策引擎的<b>菜系级新鲜度</b>惩罚
/// ——防止「连续三天吃川菜」这类菜品不重复但口味单调的情况。
/// </summary>
/// <remarks>
/// <b>派生数据</b>：任何时候可从 <see cref="MealRecord"/> 全量重算。
/// </remarks>
public sealed class UserCuisineStat
{
    public Guid UserId { get; set; }

    public Cuisine Cuisine { get; set; }

    public int EatCountTotal { get; set; }

    public DateTimeOffset? LastEatenAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }
}
