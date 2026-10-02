using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>用户。</summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? Nickname { get; set; }

    public string? AvatarUrl { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public ICollection<UserIdentity> Identities { get; set; } = [];

    public UserPreference? Preference { get; set; }
}
