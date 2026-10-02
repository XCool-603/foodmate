using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 就餐方式偏好权重。三者归一化后参与决策引擎的 <c>S_context</c> 计算。
/// </summary>
/// <remarks>
/// 用显式属性而非 <c>Dictionary&lt;DiningMode, double&gt;</c>，
/// 避免枚举作为 JSON 字典键时的序列化歧义。
/// </remarks>
public sealed class DiningModeWeights
{
    /// <summary>外卖偏好权重。</summary>
    public double Takeout { get; set; } = 1.0 / 3.0;

    /// <summary>堂食偏好权重。</summary>
    public double DineIn { get; set; } = 1.0 / 3.0;

    /// <summary>自己做偏好权重。</summary>
    public double Homemade { get; set; } = 1.0 / 3.0;

    /// <summary>取指定就餐方式的偏好权重；<see cref="DiningMode.Whatever"/> 返回中性值。</summary>
    public double For(DiningMode mode) => mode switch
    {
        DiningMode.Takeout => Takeout,
        DiningMode.DineIn => DineIn,
        DiningMode.Homemade => Homemade,
        _ => 0.5,
    };

    /// <summary>按 <paramref name="mode"/> 设置权重。</summary>
    public void Set(DiningMode mode, double value)
    {
        switch (mode)
        {
            case DiningMode.Takeout: Takeout = value; break;
            case DiningMode.DineIn: DineIn = value; break;
            case DiningMode.Homemade: Homemade = value; break;
        }
    }
}
