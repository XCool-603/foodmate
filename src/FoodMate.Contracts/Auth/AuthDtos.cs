namespace FoodMate.Contracts.Auth;

/// <summary>三端统一登录请求。</summary>
public sealed record LoginRequest
{
    /// <summary>平台：1 微信 / 2 支付宝 / 3 抖音 / 4 H5（游客）。</summary>
    public short Platform { get; init; }

    /// <summary>各端登录凭证（<c>wx.login</c> / <c>my.getAuthCode</c> / <c>tt.login</c> 返回）。</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>昵称；部分平台前端能直接拿到。</summary>
    public string? Nickname { get; init; }

    /// <summary>头像地址。</summary>
    public string? AvatarUrl { get; init; }
}

/// <summary>续期请求。</summary>
public sealed record RefreshRequest
{
    /// <summary>刷新令牌。</summary>
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>登出请求。</summary>
public sealed record LogoutRequest
{
    /// <summary>要撤销的刷新令牌；不传则撤销该用户全部设备。</summary>
    public string? RefreshToken { get; init; }
}

/// <summary>更新资料请求。</summary>
public sealed record UpdateProfileRequest
{
    /// <summary>昵称。</summary>
    public string? Nickname { get; init; }

    /// <summary>头像地址。</summary>
    public string? AvatarUrl { get; init; }
}

/// <summary>登录用户信息。</summary>
public sealed record AuthUserDto
{
    /// <summary>用户 ID。</summary>
    public Guid Id { get; init; }

    /// <summary>昵称。</summary>
    public string? Nickname { get; init; }

    /// <summary>头像。</summary>
    public string? AvatarUrl { get; init; }

    /// <summary>登录平台。</summary>
    public short Platform { get; init; }

    /// <summary>平台名称。</summary>
    public string PlatformLabel { get; init; } = string.Empty;

    /// <summary>注册时间。</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>登录 / 续期响应。</summary>
public sealed record AuthTokenResponse
{
    /// <summary>访问令牌（JWT）。</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>刷新令牌。</summary>
    public string RefreshToken { get; init; } = string.Empty;

    /// <summary>访问令牌剩余有效秒数。</summary>
    public int ExpiresIn { get; init; }

    /// <summary>是否首次注册。</summary>
    public bool IsNewUser { get; init; }

    /// <summary>是否需要完成偏好引导。</summary>
    public bool NeedsOnboarding { get; init; }

    /// <summary>用户信息；续期时为 null。</summary>
    public AuthUserDto? User { get; init; }
}

/// <summary>当前用户信息。</summary>
public sealed record MeResponse
{
    /// <summary>用户。</summary>
    public AuthUserDto User { get; init; } = new();

    /// <summary>是否需要完成偏好引导。</summary>
    public bool NeedsOnboarding { get; init; }

    /// <summary>该用户已绑定的平台（用于将来做跨端合并）。</summary>
    public IReadOnlyList<short> LinkedPlatforms { get; init; } = [];
}
