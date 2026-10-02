namespace FoodMate.Core.Exceptions;

/// <summary>
/// 业务异常。抛出后由全局异常处理器转换为
/// HTTP 200 + <see cref="Code"/> 的统一响应。
/// </summary>
public class BusinessException(int code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>业务错误码，见 <see cref="ApiErrorCode"/>。</summary>
    public int Code { get; } = code;

    /// <summary>资源不存在。</summary>
    public static BusinessException NotFound(string message)
        => new(ApiErrorCode.NotFound, message);

    /// <summary>参数校验失败。</summary>
    public static BusinessException Validation(string message)
        => new(ApiErrorCode.ValidationFailed, message);

    /// <summary>无权访问。</summary>
    public static BusinessException Forbidden(string message)
        => new(ApiErrorCode.Forbidden, message);
}
