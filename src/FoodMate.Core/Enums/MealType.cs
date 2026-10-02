namespace FoodMate.Core.Enums;

/// <summary>餐次。</summary>
public enum MealType : short
{
    /// <summary>早餐 05:00–10:00</summary>
    Breakfast = 1,

    /// <summary>午餐 10:00–15:00</summary>
    Lunch = 2,

    /// <summary>晚餐 15:00–21:00</summary>
    Dinner = 3,

    /// <summary>夜宵 21:00–05:00</summary>
    LateNight = 4,
}
