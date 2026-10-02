using FoodMate.Core.Abstractions;
using FoodMate.Core.Enums;

namespace FoodMate.Infrastructure.Identity;

/// <summary>
/// 按平台路由到对应的身份提供者。
/// </summary>
/// <remarks>
/// 新增平台只需实现 <see cref="IIdentityProvider"/> 并注册，
/// 本类与业务代码都不用改。
/// </remarks>
public sealed class IdentityProviderRegistry
{
    private readonly Dictionary<Platform, IIdentityProvider> _providers;

    public IdentityProviderRegistry(IEnumerable<IIdentityProvider> providers)
        => _providers = providers.ToDictionary(p => p.Platform);

    /// <summary>取指定平台的提供者；未注册时返回 <c>null</c>。</summary>
    public IIdentityProvider? Resolve(Platform platform)
        => _providers.GetValueOrDefault(platform);

    /// <summary>已注册的平台。</summary>
    public IReadOnlyCollection<Platform> SupportedPlatforms => _providers.Keys;
}

/// <summary>
/// 游客身份提供者（H5 / 本地开发）。
/// </summary>
/// <remarks>
/// <para>
/// 把「设备 ID 换用户」也收敛成一种标准登录方式，这样<b>全站只有一套认证机制</b>——
/// 不需要为开发环境单独开一条绕过鉴权的后门。
/// </para>
/// <para>
/// 生产环境应通过 <c>Auth:AllowGuestLogin = false</c> 关闭：
/// 游客身份无法跨设备恢复，也不适合承载用户数据。
/// </para>
/// </remarks>
public sealed class GuestIdentityProvider : IIdentityProvider
{
    /// <inheritdoc />
    public Platform Platform => Platform.H5;

    /// <inheritdoc />
    public Task<ExternalIdentity> ResolveAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new IdentityProviderException("游客登录需要提供设备标识。");
        }

        // 设备标识直接作为 OpenId
        return Task.FromResult(new ExternalIdentity(Platform.H5, code.Trim()));
    }
}
