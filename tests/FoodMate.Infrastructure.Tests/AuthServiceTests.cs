using System.Security.Cryptography;
using System.Text;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Auth;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Auth;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Tests;

/// <summary>可控的身份提供者，用于隔离测试认证逻辑本身。</summary>
internal sealed class FakeIdentityProvider(Platform platform, string openId) : IIdentityProvider
{
    public Platform Platform => platform;

    public bool ShouldFail { get; set; }

    public string OpenId { get; set; } = openId;

    public Task<ExternalIdentity> ResolveAsync(string code, CancellationToken ct = default)
    {
        if (ShouldFail)
        {
            throw new IdentityProviderException("第三方平台拒绝了这次登录。");
        }

        return Task.FromResult(new ExternalIdentity(Platform, OpenId));
    }
}

/// <summary>认证服务集成测试。</summary>
public class AuthServiceTests : SqliteTestBase
{
    private static readonly DateTimeOffset Now = new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);

    private FixedClock Clock { get; } = new(Now);

    private FakeIdentityProvider WeChatProvider { get; } = new(Platform.WeChat, "wx-openid-001");

    private AuthOptions Options { get; } = new()
    {
        Jwt = new JwtOptions
        {
            SigningKey = "test-signing-key-that-is-long-enough-32bytes",
            AccessTokenMinutes = 120,
            RefreshTokenDays = 30,
        },
    };

    private AuthService CreateService(FoodMateDbContext db, params IIdentityProvider[] extra)
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        var signingKey = new JwtSigningKey(options, NullLogger<JwtSigningKey>.Instance);

        var providers = new List<IIdentityProvider> { WeChatProvider, new GuestIdentityProvider() };
        providers.AddRange(extra);

        return new AuthService(
            db,
            new IdentityProviderRegistry(providers),
            new JwtIssuer(options, signingKey),
            Clock,
            options,
            NullLogger<AuthService>.Instance);
    }

    // ── 登录 ────────────────────────────────────────────────

    [Fact]
    public async Task 首次登录应创建用户身份与默认画像()
    {
        var service = CreateService(Db);

        var outcome = await service.LoginAsync(Platform.WeChat, "code-001", "小明");

        Assert.True(outcome.IsNewUser);
        Assert.True(outcome.NeedsOnboarding);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(outcome.Tokens.RefreshToken));
        Assert.Equal("小明", outcome.User.Nickname);

        Assert.Equal(1, await Db.UserIdentities.AsNoTracking().CountAsync());
        Assert.Equal(1, await Db.UserPreferences.AsNoTracking().CountAsync());
        Assert.Equal(1, await Db.RefreshTokens.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task 同一openId重复登录应复用用户()
    {
        var service = CreateService(Db);

        var first = await service.LoginAsync(Platform.WeChat, "code-001");
        var second = await service.LoginAsync(Platform.WeChat, "code-002");

        Assert.True(first.IsNewUser);
        Assert.False(second.IsNewUser);
        Assert.Equal(first.User.Id, second.User.Id);
        Assert.Equal(1, await Db.Users.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task 不同平台同一人应是两个独立账号()
    {
        var alipay = new FakeIdentityProvider(Platform.Alipay, "ali-openid-001");
        var service = CreateService(Db, alipay);

        var wechat = await service.LoginAsync(Platform.WeChat, "code-001");
        var ali = await service.LoginAsync(Platform.Alipay, "code-002");

        // v1 明确不做跨端账号合并
        Assert.NotEqual(wechat.User.Id, ali.User.Id);
        Assert.Equal(2, await Db.Users.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task 未配置的平台应返回明确错误()
    {
        var service = CreateService(Db);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.LoginAsync(Platform.Douyin, "code-001"));

        Assert.Equal(ApiErrorCode.UnsupportedPlatform, ex.Code);
    }

    [Fact]
    public async Task 第三方失败应转成业务错误()
    {
        WeChatProvider.ShouldFail = true;
        var service = CreateService(Db);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.LoginAsync(Platform.WeChat, "code-001"));

        Assert.Equal(ApiErrorCode.ThirdPartyUnavailable, ex.Code);
    }

    [Fact]
    public async Task 空凭证应被拒绝()
    {
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.LoginAsync(Platform.WeChat, "  "));
    }

    [Fact]
    public async Task 禁用账号应无法登录()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        var user = await Db.Users.FirstAsync(u => u.Id == login.User.Id);
        user.Status = UserStatus.Disabled;
        await Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.LoginAsync(Platform.WeChat, "code-002"));

        Assert.Equal(ApiErrorCode.Forbidden, ex.Code);
    }

    [Fact]
    public async Task 游客登录应可用且与微信账号独立()
    {
        var service = CreateService(Db);

        var guest = await service.LoginAsync(Platform.H5, "device-abc");
        var wechat = await service.LoginAsync(Platform.WeChat, "code-001");

        Assert.NotEqual(guest.User.Id, wechat.User.Id);
    }

    [Fact]
    public async Task 刷新令牌入库应为哈希而非原文()
    {
        var service = CreateService(Db);
        var outcome = await service.LoginAsync(Platform.WeChat, "code-001");

        var stored = await Db.RefreshTokens.AsNoTracking().FirstAsync();

        Assert.NotEqual(outcome.Tokens.RefreshToken, stored.TokenHash);
        Assert.Equal(IJwtIssuer.HashRefreshToken(outcome.Tokens.RefreshToken), stored.TokenHash);
    }

    // ── 续期与轮换 ──────────────────────────────────────────

    [Fact]
    public async Task 续期应签发新令牌并作废旧令牌()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        var refreshed = await service.RefreshAsync(login.Tokens.RefreshToken);

        Assert.NotEqual(login.Tokens.RefreshToken, refreshed.Tokens.RefreshToken);
        Assert.Equal(login.User.Id, refreshed.UserId);

        var tokens = await Db.RefreshTokens.AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync();

        Assert.Equal(2, tokens.Count);
        Assert.NotNull(tokens[0].RevokedAt);                       // 旧的已作废
        Assert.Null(tokens[1].RevokedAt);                          // 新的有效
        Assert.Equal(tokens[1].Id, tokens[0].ReplacedByTokenId);   // 记录轮换链
    }

    [Fact]
    public async Task 已作废的令牌再次使用应触发重放检测并撤销全部()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        // 正常续期一次
        await service.RefreshAsync(login.Tokens.RefreshToken);

        // 再用已经作废的旧令牌 —— 模拟令牌泄露后被重放
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.RefreshAsync(login.Tokens.RefreshToken));

        Assert.Equal(ApiErrorCode.RefreshTokenInvalid, ex.Code);
        Assert.Contains("所有设备", ex.Message);

        // 该用户全部令牌都应被撤销，包括刚签发的那一个
        var active = await Db.RefreshTokens.AsNoTracking()
            .CountAsync(t => t.UserId == login.User.Id && t.RevokedAt == null);

        Assert.Equal(0, active);
    }

    [Fact]
    public async Task 过期的刷新令牌应被拒绝()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        // 时间推进到过期之后
        Clock.UtcNow = Now.AddDays(31);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.RefreshAsync(login.Tokens.RefreshToken));

        Assert.Equal(ApiErrorCode.RefreshTokenInvalid, ex.Code);
    }

    [Fact]
    public async Task 无效的刷新令牌应被拒绝()
    {
        var service = CreateService(Db);
        await service.LoginAsync(Platform.WeChat, "code-001");

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.RefreshAsync("this-token-does-not-exist"));

        Assert.Equal(ApiErrorCode.RefreshTokenInvalid, ex.Code);
    }

    [Fact]
    public async Task 空刷新令牌应被拒绝()
    {
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(() => service.RefreshAsync("  "));
    }

    [Fact]
    public async Task 续期后的新令牌应能继续续期()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        var first = await service.RefreshAsync(login.Tokens.RefreshToken);
        var second = await service.RefreshAsync(first.Tokens.RefreshToken);

        Assert.NotEqual(first.Tokens.RefreshToken, second.Tokens.RefreshToken);
    }

    // ── 登出 ────────────────────────────────────────────────

    [Fact]
    public async Task 登出单设备应只撤销该令牌()
    {
        var service = CreateService(Db);

        var phone = await service.LoginAsync(Platform.WeChat, "code-001");
        var pad = await service.LoginAsync(Platform.WeChat, "code-002");

        await service.LogoutAsync(phone.User.Id, phone.Tokens.RefreshToken);

        var active = await Db.RefreshTokens.AsNoTracking()
            .Where(t => t.RevokedAt == null)
            .ToListAsync();

        Assert.Single(active);
        Assert.Equal(IJwtIssuer.HashRefreshToken(pad.Tokens.RefreshToken), active[0].TokenHash);
    }

    [Fact]
    public async Task 登出全部设备应撤销所有令牌()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");
        await service.LoginAsync(Platform.WeChat, "code-002");

        await service.LogoutAsync(login.User.Id, refreshToken: null);

        Assert.Equal(0, await Db.RefreshTokens.AsNoTracking()
            .CountAsync(t => t.UserId == login.User.Id && t.RevokedAt == null));
    }

    [Fact]
    public async Task 不能撤销他人的令牌()
    {
        var service = CreateService(Db);

        var mine = await service.LoginAsync(Platform.WeChat, "code-001");
        var other = await service.LoginAsync(Platform.H5, "device-xyz");

        // 用我的身份去登出别人的令牌
        await service.LogoutAsync(mine.User.Id, other.Tokens.RefreshToken);

        var stillActive = await Db.RefreshTokens.AsNoTracking()
            .CountAsync(t => t.UserId == other.User.Id && t.RevokedAt == null);

        Assert.Equal(1, stillActive);
    }

    // ── 资料 ────────────────────────────────────────────────

    [Fact]
    public async Task 更新资料应校验昵称长度()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateProfileAsync(login.User.Id, new string('a', 51), null));

        await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateProfileAsync(login.User.Id, "   ", null));
    }

    [Fact]
    public async Task 更新资料应生效()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        var updated = await service.UpdateProfileAsync(login.User.Id, "美食家小明", "https://x/y.jpg");

        Assert.Equal("美食家小明", updated.Nickname);
        Assert.Equal("https://x/y.jpg", updated.AvatarUrl);
    }

    [Fact]
    public async Task 完成引导后不再需要引导()
    {
        var service = CreateService(Db);
        var login = await service.LoginAsync(Platform.WeChat, "code-001");

        Assert.True(await service.NeedsOnboardingAsync(login.User.Id));

        var preference = await Db.UserPreferences.FirstAsync(p => p.UserId == login.User.Id);
        preference.OnboardingCompleted = true;
        await Db.SaveChangesAsync();

        Assert.False(await service.NeedsOnboardingAsync(login.User.Id));
    }
}

/// <summary>JWT 签发与校验测试。</summary>
public class JwtIssuerTests
{
    private static (JwtIssuer Issuer, AuthOptions Options, JwtSigningKey Key) Create(string? signingKey = null)
    {
        var options = new AuthOptions
        {
            Jwt = new JwtOptions
            {
                SigningKey = signingKey ?? "test-signing-key-that-is-long-enough-32bytes",
                AccessTokenMinutes = 60,
            },
        };

        var wrapped = Microsoft.Extensions.Options.Options.Create(options);
        var key = new JwtSigningKey(wrapped, NullLogger<JwtSigningKey>.Instance);

        return (new JwtIssuer(wrapped, key), options, key);
    }

    [Fact]
    public void 签发的令牌应包含用户与平台声明()
    {
        var (issuer, _, _) = Create();
        var userId = Guid.NewGuid();

        var (token, expiresAt) = issuer.IssueAccessToken(userId, Platform.WeChat);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var parsed = handler.ReadJwtToken(token);

        Assert.Equal(userId.ToString(), parsed.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal(((short)Platform.WeChat).ToString(), parsed.Claims.First(c => c.Type == "platform").Value);
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void 同一用户两次签发应得到不同令牌()
    {
        var (issuer, _, _) = Create();
        var userId = Guid.NewGuid();

        var (first, _) = issuer.IssueAccessToken(userId, Platform.WeChat);
        var (second, _) = issuer.IssueAccessToken(userId, Platform.WeChat);

        // jti 不同 → 令牌不同，便于将来做单令牌撤销
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void 未配置密钥时应生成临时密钥并标记()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions());
        var key = new JwtSigningKey(options, NullLogger<JwtSigningKey>.Instance);

        Assert.True(key.IsEphemeral);
        Assert.True(key.Bytes.Length >= 32);
    }

    [Fact]
    public void 配置了密钥时不应是临时的()
    {
        var (_, _, key) = Create();

        Assert.False(key.IsEphemeral);
    }

    [Fact]
    public void 刷新令牌原文应足够随机且不重复()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => IJwtIssuer.CreateRefreshToken()).ToList();

        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, t => Assert.True(t.Length >= 32));
    }

    [Fact]
    public void 刷新令牌哈希应稳定且不可逆()
    {
        const string token = "some-refresh-token";

        var first = IJwtIssuer.HashRefreshToken(token);
        var second = IJwtIssuer.HashRefreshToken(token);

        Assert.Equal(first, second);
        Assert.NotEqual(token, first);
        Assert.Equal(64, first.Length);   // SHA-256 十六进制
    }

    [Fact]
    public void 不同令牌的哈希应不同()
    {
        Assert.NotEqual(
            IJwtIssuer.HashRefreshToken("token-a"),
            IJwtIssuer.HashRefreshToken("token-b"));
    }
}

/// <summary>认证配置校验测试。</summary>
public class AuthOptionsTests
{
    [Fact]
    public void 生产环境缺少签名密钥应报错()
    {
        var options = new AuthOptions { Jwt = new JwtOptions { SigningKey = "" } };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: true));
        Assert.Contains("SigningKey", ex.Message);
    }

    [Fact]
    public void 开发环境允许缺少签名密钥()
    {
        var options = new AuthOptions { Jwt = new JwtOptions { SigningKey = "" } };

        options.Validate(isProduction: false);   // 不应抛异常
    }

    [Fact]
    public void 密钥过短应报错()
    {
        var options = new AuthOptions { Jwt = new JwtOptions { SigningKey = "too-short" } };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: false));
        Assert.Contains("32", ex.Message);
    }

    [Fact]
    public void 有效期非正数应报错()
    {
        var options = new AuthOptions
        {
            Jwt = new JwtOptions
            {
                SigningKey = "test-signing-key-that-is-long-enough-32bytes",
                AccessTokenMinutes = 0,
            },
        };

        Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: false));
    }

    [Fact]
    public void 未配置任何平台且关闭游客登录时生产环境应报错()
    {
        var options = new AuthOptions
        {
            Jwt = new JwtOptions { SigningKey = "test-signing-key-that-is-long-enough-32bytes" },
            AllowGuestLogin = false,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate(isProduction: true));
        Assert.Contains("平台", ex.Message);
    }
}

/// <summary>支付宝 RSA2 签名测试。</summary>
public class AlipaySigningTests
{
    [Fact]
    public void 签名应能用对应公钥验证通过()
    {
        using var rsa = RSA.Create(2048);

        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = "2021000000000000",
            ["method"] = "alipay.system.oauth.token",
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2",
            ["timestamp"] = "2025-06-15 12:00:00",
            ["version"] = "1.0",
            ["grant_type"] = "authorization_code",
            ["code"] = "auth-code-xyz",
        };

        var signature = AlipayIdentityProvider.Sign(parameters, privateKey);

        // 按支付宝规则重建待签串
        var content = string.Join(
            "&",
            parameters
                .Where(p => p.Key != "sign")
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));

        var verified = rsa.VerifyData(
            Encoding.UTF8.GetBytes(content),
            Convert.FromBase64String(signature),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        Assert.True(verified, "签名应能用对应公钥验证通过");
    }

    [Fact]
    public void 参数顺序不应影响签名结果()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        var a = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = "x",
            ["method"] = "m",
            ["code"] = "c",
        };

        var b = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["code"] = "c",
            ["method"] = "m",
            ["app_id"] = "x",
        };

        Assert.Equal(
            AlipayIdentityProvider.Sign(a, privateKey),
            AlipayIdentityProvider.Sign(b, privateKey));
    }

    [Fact]
    public void 空值参数不应参与签名()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        var withEmpty = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = "x",
            ["code"] = "c",
            ["empty"] = "",
        };

        var without = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = "x",
            ["code"] = "c",
        };

        Assert.Equal(
            AlipayIdentityProvider.Sign(withEmpty, privateKey),
            AlipayIdentityProvider.Sign(without, privateKey));
    }

    [Fact]
    public void 带PEM头尾的私钥也应能解析()
    {
        using var rsa = RSA.Create(2048);

        var raw = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        var pem = $"-----BEGIN PRIVATE KEY-----\n{raw}\n-----END PRIVATE KEY-----";

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1" };

        // 不应抛异常
        var signature = AlipayIdentityProvider.Sign(parameters, pem);

        Assert.False(string.IsNullOrWhiteSpace(signature));
    }

    [Fact]
    public void PKCS1格式的私钥也应能解析()
    {
        using var rsa = RSA.Create(2048);

        var pkcs1 = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1" };

        var signature = AlipayIdentityProvider.Sign(parameters, pkcs1);

        Assert.False(string.IsNullOrWhiteSpace(signature));
    }

    [Fact]
    public void 非法私钥应抛出可读错误()
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1" };

        var ex = Assert.Throws<IdentityProviderException>(
            () => AlipayIdentityProvider.Sign(parameters, "not-a-valid-key"));

        Assert.Contains("私钥", ex.Message);
    }
}
