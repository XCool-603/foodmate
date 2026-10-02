using FoodMate.Core.Enums;

namespace FoodMate.Core.Abstractions;

/// <summary>
/// 三方平台解析出的外部身份。
/// </summary>
/// <param name="Platform">来源平台。</param>
/// <param name="OpenId">平台内唯一标识。</param>
/// <param name="UnionId">微信开放平台统一标识；其他平台为 null。</param>
/// <param name="Nickname">昵称（部分平台可直接获取）。</param>
/// <param name="AvatarUrl">头像地址。</param>
public sealed record ExternalIdentity(
    Platform Platform,
    string OpenId,
    string? UnionId = null,
    string? Nickname = null,
    string? AvatarUrl = null);
