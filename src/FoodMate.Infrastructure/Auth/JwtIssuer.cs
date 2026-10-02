using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoodMate.Core.Auth;
using FoodMate.Core.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FoodMate.Infrastructure.Auth;

/// <summary>基于 HS256 的 JWT 签发器。</summary>
public sealed class JwtIssuer(
    IOptions<AuthOptions> options,
    JwtSigningKey signingKey) : IJwtIssuer
{
    private readonly AuthOptions _options = options.Value;
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <inheritdoc />
    public (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(Guid userId, Platform platform)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.Jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            // 写入平台，便于按端做风控与统计
            new("platform", ((short)platform).ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Jwt.Issuer,
            audience: _options.Jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(signingKey.Bytes),
                SecurityAlgorithms.HmacSha256));

        return (_handler.WriteToken(token), expiresAt);
    }

    /// <summary>构造 JWT 校验参数，供认证中间件复用。</summary>
    /// <remarks>必须与签发用同一把密钥，因此从同一个 <see cref="JwtSigningKey"/> 取。</remarks>
    public static TokenValidationParameters BuildValidationParameters(
        AuthOptions options,
        JwtSigningKey signingKey)
        => new()
        {
            ValidateIssuer = true,
            ValidIssuer = options.Jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKey.Bytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(options.Jwt.ClockSkewSeconds),
        };
}
