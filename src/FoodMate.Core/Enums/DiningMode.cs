namespace FoodMate.Core.Enums;

/// <summary>就餐方式。</summary>
public enum DiningMode : short
{
    /// <summary>随便（不限制）</summary>
    Whatever = 0,

    /// <summary>外卖</summary>
    Takeout = 1,

    /// <summary>堂食</summary>
    DineIn = 2,

    /// <summary>自己做</summary>
    Homemade = 3,
}
