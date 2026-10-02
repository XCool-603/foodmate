using FoodMate.Core.Abstractions;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodMate.Infrastructure.Identity;

/// <summary>
/// 游客身份服务 —— M1 阶段的临时登录方案。
/// </summary>
/// <remarks>
/// <para>
/// M1 尚未接入三端登录（那是 M4 的工作）。为了让前端能完整跑通决策闭环，
/// 这里用前端持久化的设备 ID 作为凭证，按 <c>(Platform, OpenId)</c> 找用户，
/// 找不到就建一个。
/// </para>
/// <para>
/// 这套机制与 M4 的真实登录<b>共用同一张 <c>user_identities</c> 表</b>，
/// 届时只需把 <c>ResolveAsync</c> 的入参从设备 ID 换成各端登录 code 解析出的
/// openId，其余业务代码零改动。
/// </para>
/// </remarks>
public sealed class GuestUserService(
    FoodMateDbContext db,
    IClock clock,
    ILogger<GuestUserService> logger)
{
    /// <summary>设备 ID 请求头名。</summary>
    public const string DeviceIdHeader = "X-Device-Id";

    /// <summary>按设备 ID 解析（或创建）用户，返回用户 ID。</summary>
    public async Task<Guid> ResolveAsync(
        string deviceId,
        Platform platform = Platform.H5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("设备 ID 不能为空。", nameof(deviceId));
        }

        deviceId = deviceId.Trim();

        var existing = await FindUserIdAsync(deviceId, platform, ct);
        if (existing is { } userId)
        {
            return userId;
        }

        try
        {
            return await CreateAsync(deviceId, platform, ct);
        }
        catch (DbUpdateException ex)
        {
            // 并发首次请求会撞唯一索引 —— 重查一次即可
            logger.LogDebug(ex, "并发创建设备用户，回退到重查。");

            return await FindUserIdAsync(deviceId, platform, ct)
                ?? throw new InvalidOperationException(
                    $"并发创建用户后仍查不到设备 {deviceId} 的身份记录。", ex);
        }
    }

    private async Task<Guid?> FindUserIdAsync(string deviceId, Platform platform, CancellationToken ct)
    {
        var identity = await db.UserIdentities
            .FirstOrDefaultAsync(i => i.Platform == platform && i.OpenId == deviceId, ct);

        if (identity is null)
        {
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == identity.UserId, ct);

        if (user is null)
        {
            return null;
        }

        if (user.Status == UserStatus.Disabled)
        {
            throw new InvalidOperationException("账号已被禁用。");
        }

        user.LastLoginAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return user.Id;
    }

    private async Task<Guid> CreateAsync(string deviceId, Platform platform, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var userId = Guid.NewGuid();

        db.Users.Add(new User
        {
            Id = userId,
            Nickname = "美食家",
            Status = UserStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now,
        });

        db.UserIdentities.Add(new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Platform = platform,
            OpenId = deviceId,
            CreatedAt = now,
        });

        // 新用户默认画像：辣度 2、预算 ¥15–50、无忌口、无偏好菜系
        db.UserPreferences.Add(new UserPreference
        {
            UserId = userId,
            SpicyLevel = 2,
            BudgetMinCents = 1500,
            BudgetMaxCents = 5000,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("已创建设备用户 {UserId}（平台 {Platform}）。", userId, platform);

        return userId;
    }
}
