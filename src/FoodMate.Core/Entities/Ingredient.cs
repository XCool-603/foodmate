namespace FoodMate.Core.Entities;

/// <summary>
/// 菜品食材（值对象，序列化为 <c>dishes.ingredients</c> 的 JSON 数组元素）。
/// </summary>
/// <param name="Name">食材名，如「花生米」。</param>
/// <param name="Amount">用量描述，如「50g」。可为空。</param>
/// <param name="IsCommonAllergen">是否为常见过敏原。</param>
public sealed record Ingredient(
    string Name,
    string? Amount = null,
    bool IsCommonAllergen = false);
