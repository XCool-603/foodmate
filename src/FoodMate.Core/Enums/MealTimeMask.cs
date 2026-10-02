namespace FoodMate.Core.Enums;

/// <summary>
/// 适用餐次位掩码，可组合。
/// 例：<c>Lunch | Dinner</c> = 6。
/// </summary>
[Flags]
public enum MealTimeMask : short
{
    /// <summary>无</summary>
    None = 0,

    /// <summary>早餐</summary>
    Breakfast = 1 << 0,

    /// <summary>午餐</summary>
    Lunch = 1 << 1,

    /// <summary>晚餐</summary>
    Dinner = 1 << 2,

    /// <summary>夜宵</summary>
    LateNight = 1 << 3,

    /// <summary>全时段</summary>
    All = Breakfast | Lunch | Dinner | LateNight,
}
