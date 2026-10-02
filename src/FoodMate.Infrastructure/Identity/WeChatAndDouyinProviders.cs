using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Auth;
using FoodMate.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Identity;

/// <summary>
/// 微信小程序登录：<c>wx.login</c> 拿到的 code 换 openId。
/// </summary>
/// <remarks>
/// 调用 <c>sns/jscode2session</c>。注意微信这个接口<b>即使出错也返回 HTTP 200</b>，
/// 错误信息在 body 的 <c>errcode</c> 里，必须显式检查。
/// </remarks>
public sealed class WeChatIdentityProvider(
    HttpClient http,
    IOptions<AuthOptions> options,
    ILogger<WeChatIdentityProvider> logger) : IIdentityProvider
{
    private const string Endpoint = "https://api.weixin.qq.com/sns/jscode2session";

    /// <inheritdoc />
    public Platform Platform => Platform.WeChat;

    /// <inheritdoc />
    public async Task<ExternalIdentity> ResolveAsync(string code, CancellationToken ct = default)
    {
        var credential = options.Value.WeChat;

        if (!credential.IsConfigured)
        {
            throw new IdentityProviderException(
                "微信登录未配置：请设置 Auth:WeChat:AppId 与 AppSecret。");
        }

        var url = $"{Endpoint}?appid={Uri.EscapeDataString(credential.AppId)}"
                  + $"&secret={Uri.EscapeDataString(credential.AppSecret)}"
                  + $"&js_code={Uri.EscapeDataString(code)}"
                  + "&grant_type=authorization_code";

        WeChatSession? session;

        try
        {
            session = await http.GetFromJsonAsync<WeChatSession>(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "微信 code2session 调用失败");
            throw new IdentityProviderException("微信服务器暂时不可达，请稍后重试。", ex);
        }

        if (session is null)
        {
            throw new IdentityProviderException("微信返回了空响应。");
        }

        // 微信的错误是 200 + errcode，不能靠 HTTP 状态码判断
        if (session.ErrorCode is { } errCode && errCode != 0)
        {
            logger.LogWarning("微信登录失败 errcode={Code} errmsg={Message}", errCode, session.ErrorMessage);

            throw new IdentityProviderException(errCode switch
            {
                40029 => "登录凭证已失效，请重新登录。",
                45011 => "操作过于频繁，请稍后再试。",
                40226 => "该账号存在风险，已被限制登录。",
                _ => $"微信登录失败（{errCode}）。",
            });
        }

        if (string.IsNullOrWhiteSpace(session.OpenId))
        {
            throw new IdentityProviderException("微信未返回 openid。");
        }

        return new ExternalIdentity(
            Platform.WeChat,
            session.OpenId,
            session.UnionId);
    }

    private sealed record WeChatSession
    {
        [JsonPropertyName("openid")]
        public string? OpenId { get; init; }

        [JsonPropertyName("unionid")]
        public string? UnionId { get; init; }

        [JsonPropertyName("session_key")]
        public string? SessionKey { get; init; }

        [JsonPropertyName("errcode")]
        public int? ErrorCode { get; init; }

        [JsonPropertyName("errmsg")]
        public string? ErrorMessage { get; init; }
    }
}

/// <summary>
/// 抖音小程序登录：<c>tt.login</c> 拿到的 code 换 openId。
/// </summary>
public sealed class DouyinIdentityProvider(
    HttpClient http,
    IOptions<AuthOptions> options,
    ILogger<DouyinIdentityProvider> logger) : IIdentityProvider
{
    private const string Endpoint = "https://developer.toutiao.com/api/apps/v2/jscode2session";

    /// <inheritdoc />
    public Platform Platform => Platform.Douyin;

    /// <inheritdoc />
    public async Task<ExternalIdentity> ResolveAsync(string code, CancellationToken ct = default)
    {
        var credential = options.Value.Douyin;

        if (!credential.IsConfigured)
        {
            throw new IdentityProviderException(
                "抖音登录未配置：请设置 Auth:Douyin:AppId 与 AppSecret。");
        }

        DouyinSession? session;

        try
        {
            var response = await http.PostAsJsonAsync(
                Endpoint,
                new { appid = credential.AppId, secret = credential.AppSecret, code },
                ct);

            session = await response.Content.ReadFromJsonAsync<DouyinSession>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "抖音 jscode2session 调用失败");
            throw new IdentityProviderException("抖音服务器暂时不可达，请稍后重试。", ex);
        }

        if (session is null)
        {
            throw new IdentityProviderException("抖音返回了空响应。");
        }

        if (session.ErrorNumber != 0)
        {
            logger.LogWarning(
                "抖音登录失败 err_no={Code} err_tips={Tips}", session.ErrorNumber, session.ErrorTips);

            throw new IdentityProviderException($"抖音登录失败：{session.ErrorTips ?? session.ErrorNumber.ToString()}");
        }

        var openId = session.Data?.OpenId;

        if (string.IsNullOrWhiteSpace(openId))
        {
            throw new IdentityProviderException("抖音未返回 openid。");
        }

        return new ExternalIdentity(Platform.Douyin, openId);
    }

    private sealed record DouyinSession
    {
        [JsonPropertyName("err_no")]
        public int ErrorNumber { get; init; }

        [JsonPropertyName("err_tips")]
        public string? ErrorTips { get; init; }

        [JsonPropertyName("data")]
        public DouyinSessionData? Data { get; init; }
    }

    private sealed record DouyinSessionData
    {
        [JsonPropertyName("openid")]
        public string? OpenId { get; init; }

        [JsonPropertyName("session_key")]
        public string? SessionKey { get; init; }

        [JsonPropertyName("anonymous_openid")]
        public string? AnonymousOpenId { get; init; }
    }
}
