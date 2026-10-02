using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 菜品关键属性快照，存入 <see cref="MealRecord.DishSnapshot"/>。
/// </summary>
/// <remarks>
/// 记录必须反映「当时吃的是什么」。菜品可能被改名、调整辣度或下架，
/// 因此历史记录不能依赖 <see cref="Dish"/> 的当前值。
/// </remarks>
public sealed class DishSnapshot
{
    public Cuisine Cuisine { get; set; }

    public DishCategory Category { get; set; }

    public short SpicyLevel { get; set; }

    public int? CaloriesPerServing { get; set; }

    public int? PriceCents { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>从菜品实体生成快照。</summary>
    public static DishSnapshot From(Dish dish) => new()
    {
        Cuisine = dish.Cuisine,
        Category = dish.Category,
        SpicyLevel = dish.SpicyLevel,
        CaloriesPerServing = dish.Calories,
        PriceCents = dish.PriceMinCents is null && dish.PriceMaxCents is null
            ? null
            : (dish.PriceMinCents + dish.PriceMaxCents) / 2
              ?? dish.PriceMinCents ?? dish.PriceMaxCents,
        Tags = [.. dish.Tags],
    };
}
