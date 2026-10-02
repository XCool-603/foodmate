using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 饮食记录。留存闭环的核心表，也是决策引擎偏好信号的来源。
/// </summary>
/// <remarks>
/// <see cref="DishName"/> 与 <see cref="DishSnapshot"/> 是<b>快照</b>：
/// 菜品改名或下架不影响历史记录的真实性。
/// </remarks>
public sealed class MealRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>关联菜品；纯手动输入且未入库时为 null。</summary>
    public Guid? DishId { get; set; }

    /// <summary>菜名快照。</summary>
    public string DishName { get; set; } = string.Empty;

    /// <summary>菜品关键属性快照。</summary>
    public DishSnapshot DishSnapshot { get; set; } = new();

    public MealType MealType { get; set; }

    public DiningMode DiningMode { get; set; }

    /// <summary>实际就餐时间。</summary>
    public DateTimeOffset EatenAt { get; set; }

    /// <summary>
    /// 份量系数（0.5 = 半份，2.0 = 双份）。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="double"/> 而非 <c>decimal</c>：这是一个<b>比例</b>而非金额，
    /// 不需要十进制精确语义。更关键的是 EF Core 在 SQLite 下把 <c>decimal</c>
    /// 存为 <b>TEXT</b>，会让 <c>servings &gt; 0 AND servings &lt;= 10</c>
    /// 这条检查约束退化成字符串比较——<c>'2.0' &lt;= '10'</c> 为 <b>false</b>，
    /// 于是正常的双份记录反而会被数据库拒绝。
    /// </remarks>
    public double Servings { get; set; } = 1.0;

    /// <summary>估算热量（已乘份量系数）。</summary>
    public int? Calories { get; set; }

    /// <summary>评分 1–5。决策引擎最强的偏好信号。</summary>
    public short? Rating { get; set; }

    /// <summary>还想再吃吗。</summary>
    public bool? WouldEatAgain { get; set; }

    public string? PhotoUrl { get; set; }

    public string? Note { get; set; }

    public RecordSource Source { get; set; }

    /// <summary>来自哪次决策会话。</summary>
    public Guid? DecisionSessionId { get; set; }

    /// <summary>来自哪次 AI 识别。</summary>
    public Guid? AiRecognitionLogId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }

    public Dish? Dish { get; set; }

    public DecisionSession? DecisionSession { get; set; }

    public AiRecognitionLog? AiRecognitionLog { get; set; }
}
