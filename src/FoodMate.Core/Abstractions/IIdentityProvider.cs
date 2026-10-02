using FoodMate.Core.Enums;

namespace FoodMate.Core.Abstractions;

/// <summary>
/// 三方登录身份提供者。每个平台一个实现，由 Application 层按
/// <see cref="Platform"/> 路由。
/// </summary>
public interface IIdentityProvider
{
    /// <summary>本实现负责的平台。</summary>
    Platform Platform { get; }

    /// <summary>用各端登录 code 换取外部身份。</summary>
    /// <param name="code">平台登录凭证（wx.login / my.getAuthCode / tt.login 返回）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="IdentityProviderException">平台返回错误或凭证无效。</exception>
    Task<ExternalIdentity> ResolveAsync(string code, CancellationToken ct = default);
}

/// <summary>三方身份解析失败。</summary>
public sealed class IdentityProviderException : Exception
{
    public IdentityProviderException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
