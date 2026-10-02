using System.Security.Claims;
using FoodMate.Contracts.Auth;
using FoodMate.Core;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Auth;
using FoodMate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Api.Endpoints;

/// <summary>认证端点。</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (
            LoginRequest request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var platform = Enum.IsDefined(typeof(Platform), request.Platform)
                ? (Platform)request.Platform
                : throw BusinessException.Validation($"未知的平台取值：{request.Platform}。");

            var outcome = await auth.LoginAsync(
                platform, request.Code, request.Nickname, request.AvatarUrl, ct);

            return new AuthTokenResponse
            {
                AccessToken = outcome.Tokens.AccessToken,
                RefreshToken = outcome.Tokens.RefreshToken,
                ExpiresIn = outcome.Tokens.ExpiresIn,
                IsNewUser = outcome.IsNewUser,
                NeedsOnboarding = outcome.NeedsOnboarding,
                User = ToDto(outcome.User, platform),
            };
        })
        .WithName("Login")
        .WithSummary("三端统一登录")
        .WithDescription(
            "前端先调 platform.login() 拿 code，再调本接口换令牌。"
            + "H5 / 本地开发可传 platform=4，code 传设备标识作为游客登录。")
        .WithTags("认证")
        .AllowAnonymous();

        app.MapPost("/auth/refresh", async (
            RefreshRequest request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var outcome = await auth.RefreshAsync(request.RefreshToken, ct);

            return new AuthTokenResponse
            {
                AccessToken = outcome.Tokens.AccessToken,
                RefreshToken = outcome.Tokens.RefreshToken,
                ExpiresIn = outcome.Tokens.ExpiresIn,
                User = null,
            };
        })
        .WithName("RefreshToken")
        .WithSummary("刷新令牌")
        .WithDescription(
            "采用令牌轮换：每次续期作废旧令牌并签发新的。"
            + "若已作废的令牌再次出现，视为泄露，将撤销该用户全部令牌。")
        .WithTags("认证")
        .AllowAnonymous();

        app.MapPost("/auth/logout", async (
            LogoutRequest request,
            HttpContext http,
            AuthService auth,
            CancellationToken ct) =>
        {
            var userId = CurrentUser.RequireUserId(http);
            await auth.LogoutAsync(userId, request.RefreshToken, ct);

            return new { loggedOut = true };
        })
        .WithName("Logout")
        .WithSummary("登出")
        .WithTags("认证")
        .RequireAuthorization();

        app.MapGet("/auth/me", async (
            HttpContext http,
            AuthService auth,
            FoodMateDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUser.RequireUserId(http);

            var user = await auth.GetUserAsync(userId, ct);

            var platforms = await db.UserIdentities
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .Select(i => (short)i.Platform)
                .ToListAsync(ct);

            var platform = platforms.Count > 0 ? (Platform)platforms[0] : Platform.H5;

            return new MeResponse
            {
                User = ToDto(user, platform),
                NeedsOnboarding = await auth.NeedsOnboardingAsync(userId, ct),
                LinkedPlatforms = platforms,
            };
        })
        .WithName("GetMe")
        .WithSummary("获取当前用户")
        .WithTags("认证")
        .RequireAuthorization();

        app.MapPut("/auth/me", async (
            UpdateProfileRequest request,
            HttpContext http,
            AuthService auth,
            FoodMateDbContext db,
            CancellationToken ct) =>
        {
            var userId = CurrentUser.RequireUserId(http);
            var user = await auth.UpdateProfileAsync(userId, request.Nickname, request.AvatarUrl, ct);

            var platform = await db.UserIdentities
                .AsNoTracking()
                .Where(i => i.UserId == userId)
                .Select(i => (short)i.Platform)
                .FirstOrDefaultAsync(ct);

            return ToDto(user, (Platform)platform);
        })
        .WithName("UpdateProfile")
        .WithSummary("更新昵称与头像")
        .WithTags("认证")
        .RequireAuthorization();

        return app;
    }

    private static AuthUserDto ToDto(Core.Entities.User user, Platform platform) => new()
    {
        Id = user.Id,
        Nickname = user.Nickname,
        AvatarUrl = user.AvatarUrl,
        Platform = (short)platform,
        PlatformLabel = EnumLabels.Platform(platform),
        CreatedAt = user.CreatedAt,
    };
}

/// <summary>
/// 当前用户解析。
/// </summary>
/// <remarks>
/// <b>主路径是 JWT</b>：从 <c>sub</c> 声明取用户 ID。
/// 开发环境额外支持 <c>X-Device-Id</c> 降级，方便用 curl 快速联调，
/// 但生产环境会直接拒绝——不留绕过鉴权的后门。
/// </remarks>
internal static class CurrentUser
{
    /// <summary>从 JWT 取用户 ID；取不到则抛 401。</summary>
    public static Guid RequireUserId(HttpContext http)
    {
        var subject = http.User.FindFirstValue("sub")
                      ?? http.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

        if (Guid.TryParse(subject, out var userId))
        {
            return userId;
        }

        throw new BusinessException(ApiErrorCode.Unauthorized, "未登录或登录已失效。");
    }

    /// <summary>
    /// 取用户 ID，开发环境允许设备 ID 降级。
    /// </summary>
    /// <remarks>
    /// 降级路径走的是与正式登录完全相同的 <see cref="Infrastructure.Identity.GuestIdentityProvider"/>，
    /// 因此<b>不存在绕过鉴权的后门</b>——只是省掉了前端先调一次登录接口的步骤。
    /// </remarks>
    public static async Task<Guid> ResolveAsync(
        HttpContext http,
        Infrastructure.Identity.GuestUserService guests,
        CancellationToken ct)
    {
        var subject = http.User.FindFirstValue("sub")
                      ?? http.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);

        if (Guid.TryParse(subject, out var userId))
        {
            return userId;
        }

        var environment = http.RequestServices.GetRequiredService<IHostEnvironment>();

        if (environment.IsDevelopment())
        {
            var deviceId = http.Request.Headers[Infrastructure.Identity.GuestUserService.DeviceIdHeader]
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                return await guests.ResolveAsync(deviceId, ParsePlatform(http), ct);
            }
        }

        throw new BusinessException(ApiErrorCode.Unauthorized, "未登录或登录已失效，请重新登录。");
    }

    private static Platform ParsePlatform(HttpContext http)
        => http.Request.Headers["X-Platform"].FirstOrDefault()?.ToLowerInvariant() switch
        {
            "wechat" or "mp-weixin" => Platform.WeChat,
            "alipay" or "mp-alipay" => Platform.Alipay,
            "douyin" or "toutiao" or "mp-toutiao" => Platform.Douyin,
            _ => Platform.H5,
        };
}
