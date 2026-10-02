using FoodMate.Core.Enums;

namespace FoodMate.Core;

/// <summary>餐次相关的领域规则。</summary>
public static class MealTimes
{
    /// <summary>按本地时间推断餐次。</summary>
    /// <remarks>
    /// 05:00–10:00 早餐 · 10:00–15:00 午餐 · 15:00–21:00 晚餐 · 其余夜宵。
    /// </remarks>
    public static MealType Infer(TimeOnly localTime) => localTime.Hour switch
    {
        >= 5 and < 10 => MealType.Breakfast,
        >= 10 and < 15 => MealType.Lunch,
        >= 15 and < 21 => MealType.Dinner,
        _ => MealType.LateNight,
    };

    /// <summary>取餐次对应的位掩码。</summary>
    public static MealTimeMask ToMask(MealType mealType) => mealType switch
    {
        MealType.Breakfast => MealTimeMask.Breakfast,
        MealType.Lunch => MealTimeMask.Lunch,
        MealType.Dinner => MealTimeMask.Dinner,
        MealType.LateNight => MealTimeMask.LateNight,
        _ => MealTimeMask.None,
    };

    /// <summary>中文名称，用于推荐理由文案。</summary>
    public static string Label(MealType mealType) => mealType switch
    {
        MealType.Breakfast => "早餐",
        MealType.Lunch => "午餐",
        MealType.Dinner => "晚餐",
        MealType.LateNight => "夜宵",
        _ => "这一餐",
    };

    /// <summary>按 UTC 时间与本地时区偏移推断餐次。</summary>
    public static MealType InferFromUtc(DateTimeOffset utcNow, TimeSpan localOffset)
    {
        var local = utcNow.ToOffset(localOffset);
        return Infer(TimeOnly.FromDateTime(local.DateTime));
    }
}
