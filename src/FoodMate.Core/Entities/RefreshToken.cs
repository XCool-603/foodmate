using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// 刷新令牌。
/// </summary>
/// <remarks>
/// <para>
/// 与 JWT 的分工：Access Token 是无状态 JWT，短有效期、不可撤销；
/// Refresh Token 落库、长有效期、<b>可撤销</b>——登出、改密码、风控封禁都靠它。
/// </para>
/// <para>
/// <b>只存哈希，不存原文。</b>与密码同理，数据库泄露也无法直接拿去换令牌。
/// </para>
/// </remarks>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>令牌原文的 SHA-256 十六进制哈希。</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>签发时的平台，便于按端风控。</summary>
    public Platform Platform { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>撤销时间；未撤销为 null。</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// 轮换来源：本令牌是被哪一个令牌换掉的。
    /// </summary>
    /// <remarks>
    /// 用于检测<b>令牌重放</b>——如果一个已被轮换的旧令牌又被使用，
    /// 说明它可能已泄露，此时应撤销该用户的整条令牌链。
    /// </remarks>
    public Guid? ReplacedByTokenId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }

    /// <summary>是否仍然有效。</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
