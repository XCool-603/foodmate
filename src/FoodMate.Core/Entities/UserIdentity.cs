using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 三方平台身份。多端登录的核心：<c>(Platform, OpenId)</c> 联合唯一。
/// </summary>
/// <remarks>
/// v1 不做跨端账号合并——同一人在微信和抖音是两个独立 <see cref="User"/>。
/// <see cref="UnionId"/> 先落库，为 v2 合并预留。
/// </remarks>
public sealed class UserIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Platform Platform { get; set; }

    /// <summary>各端用户唯一标识。</summary>
    public string OpenId { get; set; } = string.Empty;

    /// <summary>仅微信提供，且需绑定开放平台。</summary>
    public string? UnionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }
}
