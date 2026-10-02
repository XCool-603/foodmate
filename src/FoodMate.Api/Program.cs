using System.IdentityModel.Tokens.Jwt;
using FoodMate.Api.Endpoints;
using FoodMate.Api.Filters;
using FoodMate.Core;
using FoodMate.Core.Auth;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure;
using FoodMate.Infrastructure.Auth;
using FoodMate.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ── 服务注册 ────────────────────────────────────────────────
builder.Services.AddFoodMatePersistence(builder.Configuration);
builder.Services.AddFoodMateApplication(builder.Configuration);
builder.Services.AddFoodMateAi(builder.Configuration);
builder.Services.AddFoodMateAuth(builder.Configuration, builder.Environment.IsProduction());

// ── 认证 ────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorization();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthOptions>, JwtSigningKey>((bearer, authOptions, signingKey) =>
    {
        bearer.TokenValidationParameters = JwtIssuer.BuildValidationParameters(
            authOptions.Value, signingKey);

        // 保留原始声明名（sub 就是 sub），不做 .NET 默认的声明类型映射
        bearer.MapInboundClaims = false;

        bearer.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                // 默认的 401 响应体是 ProblemDetails，与全站统一响应格式不一致。
                // 这里接管，让前端拦截器只需处理一种错误结构。
                context.HandleResponse();

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";

                await context.Response.WriteAsJsonAsync(
                    ApiResponse<object?>.Fail(
                        ApiErrorCode.Unauthorized,
                        "未登录或登录已失效，请重新登录。",
                        context.HttpContext.TraceIdentifier),
                    context.HttpContext.RequestAborted);
            },
        };
    });

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// H5 开发时前端与 API 不同源，仅在开发环境放开
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

// ── 数据库初始化（建表 / 迁移 + 内置菜品种子数据）────────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();

    // 开发便利：模型变更后 SQLite 的 EnsureCreated 不会更新已有库，
    // 置 Database:RecreateOnStartup = true 可重建（生产环境保持 false）
    var recreate = app.Configuration.GetValue("Database:RecreateOnStartup", false);

    await initializer.InitializeAsync(recreate);
}

// ── 中间件管线 ──────────────────────────────────────────────
app.UseExceptionHandler();

// 用户上传的图片（本地存储实现）由静态文件中间件直接提供
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

// ── 端点 ────────────────────────────────────────────────────
// /health 不套统一响应结构，挂在根路径下
app.MapHealthEndpoints();

// 业务端点统一挂在 /api/v1 下，由过滤器包装为 ApiResponse<T>
var api = app.MapGroup("/api/v1").AddEndpointFilter<ApiResponseFilter>();
api.MapAuthEndpoints();
api.MapMetaEndpoints();
api.MapDishEndpoints();
api.MapDecisionEndpoints();
api.MapRecordEndpoints();
api.MapProfileEndpoints();
api.MapAiEndpoints();
api.MapUploadEndpoints();

app.Run();

/// <summary>供集成测试引用程序集。</summary>
public partial class Program;
