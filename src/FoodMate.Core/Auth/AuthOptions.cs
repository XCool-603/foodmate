using FoodMate.Core.Enums;

namespace FoodMate.Core.Auth;

/// <summary>JWT 配置。</summary>
public sealed class JwtOptions
{
    /// <summary>签发者。</summary>
    public string Issuer { get; set; } = "foodmate";

    /// <summary>受众。</summary>
    public string Audience { get; set; } = "foodmate-client";

    /// <summary>
    /// 签名密钥。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>必须通过环境变量或用户机密注入，绝不写入仓库。</b>
    /// 长度至少 32 字节（HS256 要求）。开发环境未配置时会自动生成临时密钥并告警。
    /// </remarks>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access Token 有效期（分钟）。</summary>
    public int AccessTokenMinutes { get; set; } = 120;

    /// <summary>Refresh Token 有效期（天）。</summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>时钟偏移容忍（秒），用于多实例部署时的轻微时间差。</summary>
    public int ClockSkewSeconds { get; set; } = 30;
}

/// <summary>单个平台的开放平台凭据。</summary>
public sealed class PlatformCredential
{
    /// <summary>AppId / AppKey。</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>AppSecret / AppKeySecret。</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// 应用私钥（PKCS#8 Base64），仅支付宝需要。
    /// </summary>
    /// <remarks>支付宝的接口调用需要 RSA2 签名，这是它与其他两端最大的差异。</remarks>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>是否已配置（未配置时对应平台的登录会返回明确错误）。</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret);
}

/// <summary>认证配置。</summary>
public sealed class AuthOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Auth";

    /// <summary>JWT 配置。</summary>
    public JwtOptions Jwt { get; set; } = new();

    /// <summary>微信小程序凭据。</summary>
    public PlatformCredential WeChat { get; set; } = new();

    /// <summary>支付宝小程序凭据。</summary>
    public PlatformCredential Alipay { get; set; } = new();

    /// <summary>抖音小程序凭据。</summary>
    public PlatformCredential Douyin { get; set; } = new();

    /// <summary>
    /// 是否允许游客登录（H5 / 本地开发用）。
    /// </summary>
    /// <remarks>
    /// 生产环境应设为 <c>false</c>：游客身份无法跨设备恢复，也不适合承载用户数据。
    /// </remarks>
    public bool AllowGuestLogin { get; set; } = true;

    /// <summary>校验配置；不合法时抛 <see cref="InvalidOperationException"/>。</summary>
    public void Validate(bool isProduction)
    {
        if (isProduction)
        {
            if (string.IsNullOrWhiteSpace(Jwt.SigningKey))
            {
                throw new InvalidOperationException(
                    "生产环境必须配置 Auth:Jwt:SigningKey（通过环境变量或用户机密注入）。");
            }

            if (!AllowGuestLogin && !WeChat.IsConfigured && !Alipay.IsConfigured && !Douyin.IsConfigured)
            {
                throw new InvalidOperationException(
                    "生产环境至少要配置一个平台的凭据，否则没有任何登录方式可用。");
            }
        }

        if (!string.IsNullOrWhiteSpace(Jwt.SigningKey)
            && System.Text.Encoding.UTF8.GetByteCount(Jwt.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Auth:Jwt:SigningKey 至少需要 32 字节（HS256 要求）。");
        }

        if (Jwt.AccessTokenMinutes <= 0 || Jwt.RefreshTokenDays <= 0)
        {
            throw new InvalidOperationException("Token 有效期必须为正数。");
        }
    }
}

/// <summary>一对令牌。</summary>
/// <param name="AccessToken">访问令牌（JWT）。</param>
/// <param name="RefreshToken">刷新令牌（不透明随机串）。</param>
/// <param name="ExpiresIn">访问令牌剩余有效秒数。</param>
public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn);

/// <summary>JWT 签发器。</summary>
public interface IJwtIssuer
{
    /// <summary>为用户签发访问令牌。</summary>
    (string Token, DateTimeOffset ExpiresAt) IssueAccessToken(Guid userId, Platform platform);

    /// <summary>生成一个不透明的刷新令牌原文。</summary>
    static string CreateRefreshToken()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <summary>刷新令牌入库前的哈希。</summary>
    /// <remarks>
    /// 与密码同理：<b>数据库里不存原文</b>。库被读走也无法直接拿去换令牌。
    /// </remarks>
    static string HashRefreshToken(string token)
        => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(token)));
}
