using FoodMate.Core;
using FoodMate.Core.Entities;

namespace FoodMate.Contracts.Dishes;

/// <summary>菜品简要信息。用于列表、决策结果与转盘。</summary>
public sealed record DishBriefDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public short Cuisine { get; init; }

    public string CuisineLabel { get; init; } = string.Empty;

    public short Category { get; init; }

    public string CategoryLabel { get; init; } = string.Empty;

    public short SpicyLevel { get; init; }

    public string SpicyLabel { get; init; } = string.Empty;

    public int? PriceMinCents { get; init; }

    public int? PriceMaxCents { get; init; }

    public int? Calories { get; init; }

    public string? ImageUrl { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public bool HasRecipe { get; init; }

    public short? CookMinutes { get; init; }

    public bool IsBuiltin { get; init; }

    public int Popularity { get; init; }

    /// <summary>从实体投影。</summary>
    public static DishBriefDto From(Dish dish) => new()
    {
        Id = dish.Id,
        Name = dish.Name,
        Cuisine = (short)dish.Cuisine,
        CuisineLabel = EnumLabels.Cuisine(dish.Cuisine),
        Category = (short)dish.Category,
        CategoryLabel = EnumLabels.DishCategory(dish.Category),
        SpicyLevel = dish.SpicyLevel,
        SpicyLabel = EnumLabels.SpicyLevel(dish.SpicyLevel),
        PriceMinCents = dish.PriceMinCents,
        PriceMaxCents = dish.PriceMaxCents,
        Calories = dish.Calories,
        ImageUrl = dish.ImageUrl,
        Tags = [.. dish.Tags],
        HasRecipe = dish.Recipe is not null,
        CookMinutes = dish.Recipe?.CookMinutes,
        IsBuiltin = dish.IsBuiltin,
        Popularity = dish.Popularity,
    };

    /// <summary>从决策引擎的候选视图投影。</summary>
    public static DishBriefDto FromCandidate(Core.Decision.DishCandidate dish) => new()
    {
        Id = dish.Id,
        Name = dish.Name,
        Cuisine = (short)dish.Cuisine,
        CuisineLabel = EnumLabels.Cuisine(dish.Cuisine),
        Category = (short)dish.Category,
        CategoryLabel = EnumLabels.DishCategory(dish.Category),
        SpicyLevel = dish.SpicyLevel,
        SpicyLabel = EnumLabels.SpicyLevel(dish.SpicyLevel),
        PriceMinCents = dish.PriceMinCents,
        PriceMaxCents = dish.PriceMaxCents,
        Calories = dish.Calories,
        ImageUrl = null,
        Tags = [.. dish.Tags],
        HasRecipe = dish.HasRecipe,
        CookMinutes = dish.CookMinutes,
        IsBuiltin = dish.IsBuiltin,
        Popularity = dish.Popularity,
    };
}
