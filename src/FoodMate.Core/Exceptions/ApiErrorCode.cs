namespace FoodMate.Core.Exceptions;

/// <summary>
/// 业务错误码。与 API 契约中的 <c>code</c> 字段一一对应。
/// </summary>
/// <remarks>
/// 约定：业务失败返回 HTTP 200 + 非零 <c>code</c>，
/// 只有认证/限流/服务器异常才使用 4xx/5xx。
/// </remarks>
public static class ApiErrorCode
{
    /// <summary>成功。</summary>
    public const int Success = 0;

    // ── 认证与授权 ──────────────────────────────────────
    /// <summary>未登录或 token 无效。</summary>
    public const int Unauthorized = 1001;

    /// <summary>token 已过期。</summary>
    public const int TokenExpired = 1002;

    /// <summary>无权访问该资源。</summary>
    public const int Forbidden = 1003;

    /// <summary>不支持的平台。</summary>
    public const int UnsupportedPlatform = 1004;

    /// <summary>refresh token 失效。</summary>
    public const int RefreshTokenInvalid = 1005;

    // ── 请求与资源 ──────────────────────────────────────
    /// <summary>参数校验失败。</summary>
    public const int ValidationFailed = 2001;

    /// <summary>资源不存在。</summary>
    public const int NotFound = 2002;

    /// <summary>菜品含忌口食材（需用户二次确认）。</summary>
    public const int AvoidIngredientConflict = 2003;

    /// <summary>记录不存在。</summary>
    public const int RecordNotFound = 2004;

    /// <summary>菜品已存在（重名）。</summary>
    public const int DishAlreadyExists = 2005;

    // ── AI ──────────────────────────────────────────────
    /// <summary>AI 识别失败。</summary>
    public const int AiRecognitionFailed = 3001;

    /// <summary>AI 服务超时。</summary>
    public const int AiTimeout = 3002;

    /// <summary>AI 配额超限。</summary>
    public const int AiQuotaExceeded = 3003;

    /// <summary>图片格式不支持。</summary>
    public const int ImageFormatUnsupported = 3004;

    /// <summary>图片过大。</summary>
    public const int ImageTooLarge = 3005;

    /// <summary>AI 返回解析失败。</summary>
    public const int AiParseFailed = 3006;

    // ── 业务规则 ────────────────────────────────────────
    /// <summary>没有符合条件的候选菜品。</summary>
    public const int NoCandidates = 4001;

    // ── 系统 ────────────────────────────────────────────
    /// <summary>服务器内部错误。</summary>
    public const int InternalError = 5000;

    /// <summary>数据库错误。</summary>
    public const int DatabaseError = 5001;

    /// <summary>第三方服务不可用。</summary>
    public const int ThirdPartyUnavailable = 5002;
}
