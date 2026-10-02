using FoodMate.Core.Abstractions;
using FoodMate.Core.Auth;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Auth;

/// <summary>登录结果。</summary>
/// <param name="User">用户。</param>
/// <param name="Tokens">令牌对。</param>
/// <param name="IsNewUser">是否首次注册。</param>
/// <param name="NeedsOnboarding">是否需要完成偏好引导。</param>
public sealed record LoginOutcome(User User, TokenPair Tokens, bool IsNewUser, bool NeedsOnboarding);

/// <summary>续期结果。</summary>
/// <param name="UserId">用户。</param>
/// <param name="Platform">平台。</param>
/// <param name="Tokens">新的令牌对。</param>
public sealed record RefreshOutcome(Guid UserId, Platform Platform, TokenPair Tokens);

/// <summary>
/// 认证服务：登录、续期、登出。
/// </summary>
/// <remarks>
/// <para>
/// <b>Access Token</b> 是无状态 JWT，短有效期、不可撤销，用于日常请求。
/// <b>Refresh Token</b> 落库、长有效期、<b>可撤销</b>，用于续期。
/// </para>
/// <para>
/// 续期采用<b>令牌轮换</b>：每次续期都作废旧令牌、签发新令牌。
/// 若一个<b>已被轮换</b>的旧令牌又被使用，说明它可能已泄露——
/// 此时撤销该用户的整条令牌链，强制重新登录。
/// </para>
/// </remarks>
public sealed class AuthService(
    FoodMateDbContext db,
    IdentityProviderRegistry providers,
    IJwtIssuer jwt,
    IClock clock,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger)
{
    private AuthOptions Options => options.Value;

    // ── 登录 ────────────────────────────────────────────────

    /// <summary>三端统一登录入口。</summary>
    public async Task<LoginOutcome> LoginAsync(
        Platform platform,
        string code,
        string? nickname = null,
        string? avatarUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw BusinessException.Validation("登录凭证不能为空。");
        }

        var provider = providers.Resolve(platform)
            ?? throw new BusinessException(
                ApiErrorCode.UnsupportedPlatform,
                $"暂不支持该平台登录（{platform}）。");

        ExternalIdentity external;

        try
        {
            external = await provider.ResolveAsync(code, ct);
        }
        catch (IdentityProviderException ex)
        {
            throw new BusinessException(ApiErrorCode.ThirdPartyUnavailable, ex.Message);
        }

        var (user, isNew) = await FindOrCreateUserAsync(external, nickname, avatarUrl, ct);

        if (user.Status == UserStatus.Disabled)
        {
            throw new BusinessException(ApiErrorCode.Forbidden, "账号已被禁用，请联系客服。");
        }

        user.LastLoginAt = clock.UtcNow;
        user.UpdatedAt = clock.UtcNow;

        var tokens = await IssueTokensAsync(user.Id, platform, ct);

        await db.SaveChangesAsync(ct);

        var needsOnboarding = !await db.UserPreferences
            .AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .Select(p => p.OnboardingCompleted)
            .FirstOrDefaultAsync(ct);

        logger.LogInformation(
            "登录成功 User={UserId} Platform={Platform} 新用户={IsNew}",
            user.Id, platform, isNew);

        return new LoginOutcome(user, tokens, isNew, needsOnboarding);
    }

    // ── 续期 ────────────────────────────────────────────────

    /// <summary>用刷新令牌换新的令牌对（轮换 + 重放检测）。</summary>
    public async Task<RefreshOutcome> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new BusinessException(ApiErrorCode.RefreshTokenInvalid, "缺少刷新令牌。");
        }

        var hash = IJwtIssuer.HashRefreshToken(refreshToken);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
        {
            throw new BusinessException(ApiErrorCode.RefreshTokenInvalid, "刷新令牌无效，请重新登录。");
        }

        var now = clock.UtcNow;

        // 已被轮换或撤销的令牌再次出现 → 极可能是泄露后被重放
        if (stored.RevokedAt is not null)
        {
            logger.LogWarning(
                "检测到刷新令牌重放 User={UserId} TokenId={TokenId}，撤销该用户全部令牌",
                stored.UserId, stored.Id);

            await RevokeAllAsync(stored.UserId, now, ct);
            await db.SaveChangesAsync(ct);

            throw new BusinessException(
                ApiErrorCode.RefreshTokenInvalid,
                "登录状态异常，已为你退出所有设备，请重新登录。");
        }

        if (stored.ExpiresAt <= now)
        {
            throw new BusinessException(ApiErrorCode.RefreshTokenInvalid, "登录已过期，请重新登录。");
        }

        // 轮换：旧令牌立即作废
        var tokens = await IssueTokensAsync(stored.UserId, stored.Platform, ct);

        var newHash = IJwtIssuer.HashRefreshToken(tokens.RefreshToken);
        var newToken = await db.RefreshTokens.FirstAsync(t => t.TokenHash == newHash, ct);

        stored.RevokedAt = now;
        stored.ReplacedByTokenId = newToken.Id;

        await db.SaveChangesAsync(ct);

        return new RefreshOutcome(stored.UserId, stored.Platform, tokens);
    }

    // ── 登出 ────────────────────────────────────────────────

    /// <summary>登出。传入刷新令牌则只撤销该设备，否则撤销全部设备。</summary>
    public async Task LogoutAsync(Guid userId, string? refreshToken, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await RevokeAllAsync(userId, now, ct);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("用户登出全部设备 User={UserId}", userId);
            return;
        }

        var hash = IJwtIssuer.HashRefreshToken(refreshToken);

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, ct);

        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    // ── 资料 ────────────────────────────────────────────────

    /// <summary>取当前用户。</summary>
    public async Task<User> GetUserAsync(Guid userId, CancellationToken ct = default)
        => await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
           ?? throw BusinessException.NotFound("用户不存在。");

    /// <summary>更新昵称与头像。</summary>
    public async Task<User> UpdateProfileAsync(
        Guid userId,
        string? nickname,
        string? avatarUrl,
        CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw BusinessException.NotFound("用户不存在。");

        if (nickname is not null)
        {
            var trimmed = nickname.Trim();

            if (trimmed.Length is 0 or > 50)
            {
                throw BusinessException.Validation("昵称长度需在 1–50 字之间。");
            }

            user.Nickname = trimmed;
        }

        if (avatarUrl is not null)
        {
            if (avatarUrl.Length > 500)
            {
                throw BusinessException.Validation("头像地址过长。");
            }

            user.AvatarUrl = avatarUrl;
        }

        user.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return user;
    }

    /// <summary>该用户是否需要完成偏好引导。</summary>
    public async Task<bool> NeedsOnboardingAsync(Guid userId, CancellationToken ct = default)
        => !await db.UserPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.OnboardingCompleted)
            .FirstOrDefaultAsync(ct);

    // ── 内部实现 ────────────────────────────────────────────

    private async Task<(User User, bool IsNew)> FindOrCreateUserAsync(
        ExternalIdentity external,
        string? nickname,
        string? avatarUrl,
        CancellationToken ct)
    {
        var identity = await db.UserIdentities
            .FirstOrDefaultAsync(i => i.Platform == external.Platform && i.OpenId == external.OpenId, ct);

        if (identity is not null)
        {
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Id == identity.UserId, ct)
                ?? throw new InvalidOperationException(
                    $"身份 {identity.Id} 指向了不存在的用户 {identity.UserId}。");

            // 微信的 unionid 可能后补，取到就存下来，为将来跨端合并预留
            if (identity.UnionId is null && external.UnionId is not null)
            {
                identity.UnionId = external.UnionId;
            }

            return (existing, false);
        }

        var now = clock.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Nickname = string.IsNullOrWhiteSpace(nickname) ? DefaultNickname(external.Platform) : nickname.Trim(),
            AvatarUrl = avatarUrl,
            Status = UserStatus.Active,
            LastLoginAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Users.Add(user);

        db.UserIdentities.Add(new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Platform = external.Platform,
            OpenId = external.OpenId,
            UnionId = external.UnionId,
            CreatedAt = now,
        });

        // 新用户给一份默认画像，保证决策页立即可用
        db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            SpicyLevel = 2,
            BudgetMinCents = 1500,
            BudgetMaxCents = 5000,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "新建用户 User={UserId} Platform={Platform}", user.Id, external.Platform);

        return (user, true);
    }

    private async Task<TokenPair> IssueTokensAsync(Guid userId, Platform platform, CancellationToken ct)
    {
        var (accessToken, expiresAt) = jwt.IssueAccessToken(userId, platform);

        var refreshToken = IJwtIssuer.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = IJwtIssuer.HashRefreshToken(refreshToken),
            Platform = platform,
            ExpiresAt = clock.UtcNow.AddDays(Options.Jwt.RefreshTokenDays),
            CreatedAt = clock.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        var expiresIn = (int)Math.Max(0, (expiresAt - clock.UtcNow).TotalSeconds);

        return new TokenPair(accessToken, refreshToken, expiresIn);
    }

    private async Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAt = now;
        }
    }

    private static string DefaultNickname(Platform platform) => platform switch
    {
        Platform.WeChat => "微信用户",
        Platform.Alipay => "支付宝用户",
        Platform.Douyin => "抖音用户",
        _ => "美食家",
    };
}
