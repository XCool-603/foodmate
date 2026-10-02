namespace FoodMate.Core.Entities;

/// <summary>
/// 菜谱（1:1 于 <see cref="Dish"/>）。「自己做」模式下必需。
/// </summary>
public sealed class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DishId { get; set; }

    /// <summary>菜谱对应的份数。</summary>
    public short Servings { get; set; } = 2;

    /// <summary>烹饪时长（分钟）。</summary>
    public short CookMinutes { get; set; }

    /// <summary>难度 1 简单 / 2 中等 / 3 复杂。</summary>
    public short Difficulty { get; set; } = 1;

    public List<RecipeStep> Steps { get; set; } = [];

    public string? Tips { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public Dish? Dish { get; set; }
}
