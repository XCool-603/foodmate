using FoodMate.Core;
using Microsoft.AspNetCore.Http;

namespace FoodMate.Api.Filters;

/// <summary>
/// 把端点返回的业务对象统一包装为 <see cref="ApiResponse{T}"/>。
/// </summary>
/// <remarks>
/// <para>
/// 端点只需返回普通 DTO 或 <see cref="PagedResult{T}"/>，
/// 无需手动构造 <c>code/message</c> 外层。
/// </para>
/// <para>
/// 已经返回 <see cref="IResult"/>（如 <c>TypedResults.Ok</c>）或已是
/// <see cref="ApiResponse{T}"/> 的响应会原样放行——这让 <c>/health</c>
/// 这类需要裸结构的端点可以绕过包装。
/// </para>
/// </remarks>
public sealed class ApiResponseFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var result = await next(context);

        // 已经是完整响应，或端点自行控制了 HTTP 语义 → 放行
        if (result is IResult or ApiResponse<object> || IsApiResponse(result))
        {
            return result;
        }

        return ApiResponse<object?>.Ok(result, context.HttpContext.TraceIdentifier);
    }

    private static bool IsApiResponse(object? value)
        => value is not null
           && value.GetType().IsGenericType
           && value.GetType().GetGenericTypeDefinition() == typeof(ApiResponse<>);
}
