namespace FoodMate.Core.Entities;

/// <summary>菜谱步骤（值对象，序列化为 <c>recipes.steps</c> 的 JSON 数组元素）。</summary>
/// <param name="Order">步骤序号，从 1 开始。</param>
/// <param name="Text">步骤描述。</param>
/// <param name="DurationMinutes">该步骤耗时（分钟）。可为空。</param>
public sealed record RecipeStep(
    int Order,
    string Text,
    int? DurationMinutes = null);
