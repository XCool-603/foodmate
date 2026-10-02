using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Auth;
using FoodMate.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Identity;

/// <summary>
/// 支付宝小程序登录。
/// </summary>
/// <remarks>
/// <para>
/// 三端里最麻烦的一个：支付宝的网关接口要求 <b>RSA2 签名</b>——
/// 把参数按 key 排序拼成 <c>k1=v1&amp;k2=v2</c>，再用应用私钥做 SHA256withRSA。
/// 微信和抖音都只是普通 HTTP 调用。
/// </para>
/// <para>
/// 用的是 <c>alipay.system.oauth.token</c>（<c>grant_type=authorization_code</c>），
/// 返回的 <c>user_id</c> 就是该用户在支付宝体系内的唯一标识，作为我们的 OpenId。
/// </para>
/// </remarks>
public sealed class AlipayIdentityProvider(
    HttpClient http,
    IOptions<AuthOptions> options,
    ILogger<AlipayIdentityProvider> logger) : IIdentityProvider
{
    private const string Gateway = "https://openapi.alipay.com/gateway.do";
    private const string Method = "alipay.system.oauth.token";
    private const string SuccessKey = "alipay_system_oauth_token_response";
    private const string ErrorKey = "error_response";

    /// <inheritdoc />
    public Platform Platform => Platform.Alipay;

    /// <inheritdoc />
    public async Task<ExternalIdentity> ResolveAsync(string code, CancellationToken ct = default)
    {
        var credential = options.Value.Alipay;

        if (!credential.IsConfigured)
        {
            throw new IdentityProviderException(
                "支付宝登录未配置：请设置 Auth:Alipay:AppId 与 AppSecret。");
        }

        if (string.IsNullOrWhiteSpace(credential.PrivateKey))
        {
            throw new IdentityProviderException(
                "支付宝登录需要应用私钥：请设置 Auth:Alipay:PrivateKey（PKCS#8 Base64）。");
        }

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = credential.AppId,
            ["method"] = Method,
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2",
            ["timestamp"] = BeijingNow(),
            ["version"] = "1.0",
            ["grant_type"] = "authorization_code",
            ["code"] = code,
        };

        parameters["sign"] = Sign(parameters, credential.PrivateKey);

        using var content = new FormUrlEncodedContent(parameters);

        string body;

        try
        {
            var response = await http.PostAsync(Gateway, content, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "支付宝网关调用失败");
            throw new IdentityProviderException("支付宝服务器暂时不可达，请稍后重试。", ex);
        }

        return Parse(body);
    }

    private ExternalIdentity Parse(string body)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "支付宝返回无法解析：{Body}", Truncate(body));
            throw new IdentityProviderException("支付宝返回格式异常。", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            // 支付宝把业务错误放在 error_response 里，HTTP 状态码仍是 200
            if (root.TryGetProperty(ErrorKey, out var error))
            {
                var subMessage = error.TryGetProperty("sub_msg", out var sub) ? sub.GetString() : null;
                var message = error.TryGetProperty("msg", out var msg) ? msg.GetString() : null;

                logger.LogWarning("支付宝登录失败：{Message} / {Sub}", message, subMessage);

                throw new IdentityProviderException(
                    $"支付宝登录失败：{subMessage ?? message ?? "未知错误"}");
            }

            if (!root.TryGetProperty(SuccessKey, out var success))
            {
                throw new IdentityProviderException("支付宝返回中缺少预期的响应字段。");
            }

            // user_id 是支付宝体系内的唯一标识；部分场景下叫 alipay_user_id
            var userId = GetString(success, "user_id") ?? GetString(success, "alipay_user_id");

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new IdentityProviderException("支付宝未返回 user_id。");
            }

            return new ExternalIdentity(Platform.Alipay, userId);
        }
    }

    /// <summary>按支付宝规则计算 RSA2 签名。</summary>
    /// <remarks>
    /// 规则：剔除 <c>sign</c> 与空值 → 按 key 字典序排序 → 拼成 <c>k=v&amp;k=v</c>
    /// → 用应用私钥做 SHA256withRSA → Base64。
    /// </remarks>
    public static string Sign(IReadOnlyDictionary<string, string> parameters, string privateKeyBase64)
    {
        var content = string.Join(
            "&",
            parameters
                .Where(p => p.Key != "sign" && !string.IsNullOrEmpty(p.Value))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));

        using var rsa = RSA.Create();

        if (!TryImportPrivateKey(rsa, privateKeyBase64))
        {
            throw new IdentityProviderException(
                "支付宝应用私钥无法解析。请确认是 PKCS#8 或 PKCS#1 的 Base64 内容。");
        }

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(content),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// 导入私钥。
    /// </summary>
    /// <remarks>
    /// 支付宝控制台给的密钥格式不统一，PKCS#8 与 PKCS#1 都有人用，这里两种都试。
    /// 同时容忍 PEM 的 <c>-----BEGIN</c> 头尾与换行。
    /// </remarks>
    private static bool TryImportPrivateKey(RSA rsa, string key)
    {
        var cleaned = key
            .Replace("-----BEGIN PRIVATE KEY-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----END PRIVATE KEY-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----BEGIN RSA PRIVATE KEY-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----END RSA PRIVATE KEY-----", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();

        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(cleaned);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            rsa.ImportPkcs8PrivateKey(bytes, out _);
            return true;
        }
        catch (CryptographicException)
        {
            // 继续尝试 PKCS#1
        }

        try
        {
            rsa.ImportRSAPrivateKey(bytes, out _);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>支付宝要求 <c>yyyy-MM-dd HH:mm:ss</c> 且为北京时间。</summary>
    private static string BeijingNow()
        => DateTimeOffset.UtcNow
            .ToOffset(TimeSpan.FromHours(8))
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value)
        => value.Length <= 500 ? value : value[..500] + "…";
}
