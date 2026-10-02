using FoodMate.Core;
using FoodMate.Core.Entities;

namespace FoodMate.Contracts.Records;

/// <summary>饮食记录实体到 DTO 的投影。</summary>
public static class RecordMapper
{
    /// <summary>投影为记录 DTO。</summary>
    public static RecordDto ToDto(this MealRecord record) => new()
    {
        Id = record.Id,
        DishId = record.DishId,
        DishName = record.DishName,
        DishSnapshot = record.DishSnapshot.ToDto(),
        MealType = (short)record.MealType,
        MealTypeLabel = EnumLabels.MealType(record.MealType),
        DiningMode = (short)record.DiningMode,
        DiningModeLabel = EnumLabels.DiningMode(record.DiningMode),
        EatenAt = record.EatenAt,
        Servings = record.Servings,
        Calories = record.Calories,
        Rating = record.Rating,
        WouldEatAgain = record.WouldEatAgain,
        PhotoUrl = record.PhotoUrl,
        Note = record.Note,
        Source = (short)record.Source,
        SourceLabel = EnumLabels.RecordSource(record.Source),
    };

    /// <summary>投影为菜品快照 DTO。</summary>
    public static DishSnapshotDto ToDto(this DishSnapshot snapshot) => new()
    {
        Cuisine = (short)snapshot.Cuisine,
        CuisineLabel = EnumLabels.Cuisine(snapshot.Cuisine),
        Category = (short)snapshot.Category,
        CategoryLabel = EnumLabels.DishCategory(snapshot.Category),
        SpicyLevel = snapshot.SpicyLevel,
        CaloriesPerServing = snapshot.CaloriesPerServing,
        PriceCents = snapshot.PriceCents,
        Tags = [.. snapshot.Tags],
    };

    /// <summary>投影为待评分提醒项。</summary>
    public static PendingRatingDto ToPendingDto(this MealRecord record, DateTimeOffset now)
    {
        var daysAgo = (int)Math.Floor((now - record.EatenAt).TotalDays);

        return new PendingRatingDto
        {
            Id = record.Id,
            DishName = record.DishName,
            PhotoUrl = record.PhotoUrl,
            EatenAt = record.EatenAt,
            DaysAgo = Math.Max(0, daysAgo),
        };
    }
}
