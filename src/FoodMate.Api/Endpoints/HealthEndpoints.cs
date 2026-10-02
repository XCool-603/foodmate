using FoodMate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Api.Endpoints;

/// <summary>
/// 健康检查端点。
/// </summary>
/// <remarks>
/// 这两个端点<b>不套统一响应结构</b>——它们返回 <see cref="IResult"/>，
/// 由 <c>ApiResponseFilter</c> 原样放行，便于监控系统直接解析。
/// </remarks>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", (IHostEnvironment env) => TypedResults.Ok(new
        {
            status = "healthy",
            name = "美食伴侣",
            version = AppInfo.Version,
            environment = env.EnvironmentName,
            serverTime = DateTimeOffset.UtcNow,
        }))
        .WithName("Health")
        .WithTags("系统");

        app.MapGet("/health/ready", async (FoodMateDbContext db, CancellationToken ct) =>
        {
            var databaseOk = await db.Database.CanConnectAsync(ct);
            var dishCount = databaseOk
                ? await db.Dishes.AsNoTracking().CountAsync(ct)
                : 0;

            var payload = new
            {
                status = databaseOk ? "ready" : "not-ready",
                checks = new
                {
                    database = databaseOk ? "ok" : "failed",
                    dishCount,
                },
                serverTime = DateTimeOffset.UtcNow,
            };

            return databaseOk
                ? Results.Ok(payload)
                : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("HealthReady")
        .WithTags("系统");

        return app;
    }
}
