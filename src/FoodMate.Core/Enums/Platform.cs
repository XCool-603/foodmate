namespace FoodMate.Core.Enums;

/// <summary>
/// 登录平台。
/// 枚举值一旦发布不可复用或修改，只能追加。
/// </summary>
public enum Platform : short
{
    /// <summary>微信小程序</summary>
    WeChat = 1,

    /// <summary>支付宝小程序</summary>
    Alipay = 2,

    /// <summary>抖音小程序</summary>
    Douyin = 3,

    /// <summary>H5（无小程序宿主）</summary>
    H5 = 4,
}
