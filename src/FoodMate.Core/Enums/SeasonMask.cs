namespace FoodMate.Core.Enums;

/// <summary>
/// 适用季节位掩码。<see cref="AllYear"/>（0）表示四季皆宜。
/// </summary>
[Flags]
public enum SeasonMask : short
{
    /// <summary>四季皆宜</summary>
    AllYear = 0,

    /// <summary>春</summary>
    Spring = 1 << 0,

    /// <summary>夏</summary>
    Summer = 1 << 1,

    /// <summary>秋</summary>
    Autumn = 1 << 2,

    /// <summary>冬</summary>
    Winter = 1 << 3,
}
