using FoodMate.Contracts.Ai;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Ai;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Ai;
using FoodMate.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FoodMate.Api.Endpoints;

/// <summary>图片上传端点。</summary>
public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/uploads/image", async (
            HttpContext http,
            IFormFile file,
            IImageStorage storage,
            GuestUserService users,
            IOptions<AiOptions> options,
            CancellationToken ct) =>
        {
            var settings = options.Value;
            var userId = await CurrentUser.ResolveAsync(http, users, ct);

            if (file.Length <= 0)
            {
                throw new BusinessException(ApiErrorCode.ValidationFailed, "上传的文件为空。");
            }

            if (file.Length > settings.MaxImageBytes)
            {
                throw new BusinessException(
                    ApiErrorCode.ImageTooLarge,
                    $"图片不能超过 {settings.MaxImageBytes / 1024 / 1024} MB，请压缩后重试。");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!settings.AllowedImageExtensions.Contains(extension))
            {
                throw new BusinessException(
                    ApiErrorCode.ImageFormatUnsupported,
                    $"只支持 {string.Join("、", settings.AllowedImageExtensions)} 格式。");
            }

            await using var stream = file.OpenReadStream();

            var stored = await storage.SaveAsync(
                userId,
                new ImageUpload(stream, file.FileName, file.ContentType, file.Length),
                ct);

            return stored;
        })
        .WithName("UploadImage")
        .WithSummary("上传图片")
        .WithDescription("返回相对路径，前端用 API 基地址拼接即可访问。")
        .WithTags("上传")
        // 小程序上传不带 CSRF token，且鉴权由设备 ID / JWT 承担
        .DisableAntiforgery();

        return app;
    }
}

/// <summary>AI 端点。</summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/ai/recognize", async (
            RecognizeRequest request,
            HttpContext http,
            AiRecognitionService ai,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var outcome = await ai.RecognizeAsync(userId, request.ImageUrl, ct);

            return outcome.Result;
        })
        .WithName("RecognizeDishes")
        .WithSummary("识别图片中的菜品")
        .WithDescription(
            "只识别不入库，同时写一条识别日志。用户确认后才调用 /ai/recognize/{logId}/confirm 落库。"
            + "模型不可用时返回 3001/3002，前端应降级为手动输入。")
        .WithTags("AI");

        app.MapPost("/ai/recognize/{logId:guid}/confirm", async (
            Guid logId,
            ConfirmRecognitionRequest request,
            HttpContext http,
            AiRecognitionService ai,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var outcome = await ai.ConfirmAsync(userId, logId, request, ct);

            return new ConfirmRecognitionResponse
            {
                RecordIds = outcome.RecordIds,
                CreatedDishIds = outcome.CreatedDishIds,
                CorrectedCount = outcome.CorrectedCount,
                WasCorrected = outcome.WasCorrected,
            };
        })
        .WithName("ConfirmRecognition")
        .WithSummary("确认识别结果并写入记录")
        .WithDescription(
            "用户的每一次修正都会沉淀到识别日志的 corrected_result，"
            + "这是优化识别准确率的核心数据资产。")
        .WithTags("AI");

        app.MapPost("/ai/recipe", async (
            GenerateRecipeRequest request,
            HttpContext http,
            AiRecognitionService ai,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            return await ai.GenerateRecipeAsync(userId, request, ct);
        })
        .WithName("GenerateRecipe")
        .WithSummary("生成菜谱")
        .WithDescription("菜品库已有菜谱时直接返回缓存，不调用模型。")
        .WithTags("AI");

        app.MapPost("/ai/shopping-list", async (
            ShoppingListRequest request,
            HttpContext http,
            AiRecognitionService ai,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            return await ai.GenerateShoppingListAsync(userId, request, ct);
        })
        .WithName("GenerateShoppingList")
        .WithSummary("生成买菜清单")
        .WithTags("AI");

        return app;
    }
}
