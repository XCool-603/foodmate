using System.Security.Cryptography;
using System.Text;
using FoodMate.Core.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Auth;

/// <summary>
/// JWT 签名密钥的<b>唯一来源</b>。
/// </summary>
/// <remarks>
/// <para>
/// 签发与校验必须用同一把密钥。如果让 <c>JwtIssuer</c> 各自决定「没配置就随机生成」，
/// 签发方和校验方会拿到两把不同的密钥，token 一签发就校验不过——
/// 这种 bug 在联调时极难排查。因此把密钥收敛成单例。
/// </para>
/// <para>
/// 开发环境未配置时生成临时密钥并告警；生产环境由
/// <see cref="AuthOptions.Validate"/> 在启动时直接拦下。
/// </para>
/// </remarks>
public sealed class JwtSigningKey
{
    /// <summary>密钥字节。</summary>
    public byte[] Bytes { get; }

    /// <summary>是否为运行时生成的临时密钥。</summary>
    public bool IsEphemeral { get; }

    public JwtSigningKey(IOptions<AuthOptions> options, ILogger<JwtSigningKey> logger)
    {
        var configured = options.Value.Jwt.SigningKey;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            Bytes = Encoding.UTF8.GetBytes(configured);
            IsEphemeral = false;
            return;
        }

        Bytes = RandomNumberGenerator.GetBytes(48);
        IsEphemeral = true;

        logger.LogWarning(
            "Auth:Jwt:SigningKey 未配置，已生成临时密钥。"
            + "进程重启后所有 token 会失效——本地开发可以接受，生产环境必须显式配置。");
    }
}
