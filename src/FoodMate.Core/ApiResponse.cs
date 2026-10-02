using FoodMate.Core.Exceptions;

namespace FoodMate.Core;

/// <summary>统一响应外层结构。所有业务接口（<c>/health</c> 除外）都返回此结构。</summary>
/// <typeparam name="T">业务数据类型。</typeparam>
public sealed record ApiResponse<T>
{
    /// <summary>业务错误码；0 表示成功。</summary>
    public int Code { get; init; }

    /// <summary>面向开发者的描述。</summary>
    public string Message { get; init; } = "ok";

    /// <summary>业务数据；失败时为 <c>null</c>。</summary>
    public T? Data { get; init; }

    /// <summary>链路追踪 ID，排查问题时提供给后端。</summary>
    public string? TraceId { get; init; }

    /// <summary>构造成功响应。</summary>
    public static ApiResponse<T> Ok(T data, string? traceId = null) => new()
    {
        Code = ApiErrorCode.Success,
        Message = "ok",
        Data = data,
        TraceId = traceId,
    };

    /// <summary>构造失败响应。</summary>
    public static ApiResponse<T> Fail(int code, string message, string? traceId = null) => new()
    {
        Code = code,
        Message = message,
        Data = default,
        TraceId = traceId,
    };
}
