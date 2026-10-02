using FoodMate.Core;
using FoodMate.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace FoodMate.Api.Filters;

/// <summary>
/// 全局异常处理器：把未处理异常转换为统一响应结构。
/// </summary>
/// <remarks>
/// 映射规则见 API 契约 §1.2：业务异常返回 HTTP 200 + 业务码，
/// 只有真正的基础设施故障才返回 5xx。
/// </remarks>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        var (code, status, message) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "未处理异常 {TraceId} {Path}", traceId, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "业务异常 {TraceId} {Code} {Path}", traceId, code, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/json; charset=utf-8";

        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse<object?>.Fail(code, message, traceId),
            cancellationToken);

        return true;
    }

    private static (int Code, int Status, string Message) Map(Exception exception) => exception switch
    {
        BusinessException be => (be.Code, StatusCodes.Status200OK, be.Message),

        ArgumentException or ArgumentNullException
            => (ApiErrorCode.ValidationFailed, StatusCodes.Status200OK, exception.Message),

        KeyNotFoundException
            => (ApiErrorCode.NotFound, StatusCodes.Status200OK, "资源不存在"),

        UnauthorizedAccessException
            => (ApiErrorCode.Unauthorized, StatusCodes.Status401Unauthorized, "未登录或登录已失效"),

        OperationCanceledException
            => (ApiErrorCode.InternalError, 499, "请求已取消"),

        _ => (ApiErrorCode.InternalError, StatusCodes.Status500InternalServerError, "服务开小差了，请稍后再试"),
    };
}
