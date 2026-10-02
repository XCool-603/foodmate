using FoodMate.Contracts.Profile;
using FoodMate.Contracts.Records;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Enums;
using FoodMate.Core.Profile;
using FoodMate.Infrastructure.Identity;
using FoodMate.Infrastructure.Profile;
using FoodMate.Infrastructure.Records;
using Microsoft.Extensions.Options;

namespace FoodMate.Api.Endpoints;

/// <summary>饮食记录端点。</summary>
public static class RecordEndpoints
{
    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/records", async (
            CreateRecordRequest request,
            HttpContext http,
            RecordService records,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var record = await records.CreateAsync(userId, request, ct);

            return record.ToDto();
        })
        .WithName("CreateRecord")
        .WithSummary("创建饮食记录")
        .WithDescription(
            "记录是「我吃了什么」的客观事实。若菜品含用户忌口食材，"
            + "返回业务码 2003 让前端确认一次；用户确认后带 force=true 重试即可写入。")
        .WithTags("记录");

        app.MapGet("/records", async (
            HttpContext http,
            RecordService records,
            GuestUserService users,
            int page = 1,
            int pageSize = 20,
            DateOnly? from = null,
            DateOnly? to = null,
            short? mealType = null,
            Guid? dishId = null,
            bool? hasRating = null,
            CancellationToken ct = default) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);

            var result = await records.ListAsync(userId, new RecordQuery
            {
                Page = page,
                PageSize = pageSize,
                From = from,
                To = to,
                MealType = mealType,
                DishId = dishId,
                HasRating = hasRating,
            }, ct);

            return PagedResult<RecordDto>.Create(
                [.. result.Items.Select(r => r.ToDto())],
                result.Total,
                result.Page,
                result.PageSize);
        })
        .WithName("ListRecords")
        .WithSummary("记录列表（按就餐时间倒序）")
        .WithTags("记录");

        app.MapGet("/records/pending-rating", async (
            HttpContext http,
            RecordService records,
            GuestUserService users,
            IClock clock,
            int limit = 3,
            CancellationToken ct = default) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var pending = await records.PendingRatingAsync(userId, limit, ct);

            return pending.Select(r => r.ToPendingDto(clock.UtcNow)).ToList();
        })
        .WithName("PendingRating")
        .WithSummary("待评分提醒")
        .WithDescription("返回最近 7 天内未评分的记录，用于首页/记录页主动提醒。")
        .WithTags("记录");

        app.MapGet("/records/stats", async (
            HttpContext http,
            RecordStatsService stats,
            GuestUserService users,
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            return await stats.GetStatsAsync(userId, from, to, ct);
        })
        .WithName("RecordStats")
        .WithSummary("统计报表")
        .WithTags("记录");

        app.MapGet("/records/{id:guid}", async (
            Guid id,
            HttpContext http,
            RecordService records,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var record = await records.GetAsync(userId, id, ct);

            return record.ToDto();
        })
        .WithName("GetRecord")
        .WithSummary("记录详情")
        .WithTags("记录");

        app.MapPut("/records/{id:guid}", async (
            Guid id,
            UpdateRecordRequest request,
            HttpContext http,
            RecordService records,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var record = await records.UpdateAsync(userId, id, request, ct);

            return record.ToDto();
        })
        .WithName("UpdateRecord")
        .WithSummary("更新记录")
        .WithTags("记录");

        app.MapPost("/records/{id:guid}/rating", async (
            Guid id,
            RateRecordRequest request,
            HttpContext http,
            RecordService records,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var record = await records.RateAsync(userId, id, request.Rating, request.WouldEatAgain, ct);

            return record.ToDto();
        })
        .WithName("RateRecord")
        .WithSummary("快速评分")
        .WithTags("记录");

        app.MapDelete("/records/{id:guid}", async (
            Guid id,
            HttpContext http,
            RecordService records,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            await records.DeleteAsync(userId, id, ct);

            return new { id, deleted = true };
        })
        .WithName("DeleteRecord")
        .WithSummary("删除记录（软删除）")
        .WithTags("记录");

        return app;
    }
}

/// <summary>口味画像端点。</summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me/preference", async (
            HttpContext http,
            PreferenceService preferences,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var preference = await preferences.GetOrCreateAsync(userId, ct);

            return ToDto(preference);
        })
        .WithName("GetPreference")
        .WithSummary("获取口味画像")
        .WithDescription("从未设置过时返回默认画像，不返回 404，前端无需处理空态。")
        .WithTags("画像");

        app.MapPut("/me/preference", async (
            UpdatePreferenceRequest request,
            HttpContext http,
            PreferenceService preferences,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var preference = await preferences.UpdateAsync(userId, request, ct);

            return ToDto(preference);
        })
        .WithName("UpdatePreference")
        .WithSummary("更新口味画像")
        .WithDescription("修改忌口会立即影响后续决策的硬过滤，前端应提示用户。")
        .WithTags("画像");

        app.MapPost("/me/preference/onboarding", async (
            OnboardingRequest request,
            HttpContext http,
            PreferenceService preferences,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var preference = await preferences.CompleteOnboardingAsync(userId, request, ct);

            return ToDto(preference);
        })
        .WithName("CompleteOnboarding")
        .WithSummary("完成新用户偏好引导")
        .WithTags("画像");

        app.MapGet("/me/preference/suggestion", async (
            HttpContext http,
            PreferenceService preferences,
            GuestUserService users,
            IOptions<PreferenceLearningOptions> learningOptions,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var suggestion = await preferences.SuggestAsync(userId, ct);

            if (suggestion is null)
            {
                return new PreferenceSuggestionDto
                {
                    HasAny = false,
                    RequiredSampleSize = learningOptions.Value.MinSampleSize,
                };
            }

            return new PreferenceSuggestionDto
            {
                SpicyLevel = suggestion.SpicyLevel,
                BudgetMinCents = suggestion.BudgetMinCents,
                BudgetMaxCents = suggestion.BudgetMaxCents,
                PreferredCuisines = suggestion.PreferredCuisines is null
                    ? null
                    : [.. suggestion.PreferredCuisines.Select(c => (short)c)],
                PreferredCuisineLabels = suggestion.PreferredCuisines is null
                    ? null
                    : [.. suggestion.PreferredCuisines.Select(EnumLabels.Cuisine)],
                SampleSize = suggestion.SampleSize,
                HasAny = suggestion.HasAny,
                RequiredSampleSize = learningOptions.Value.MinSampleSize,
            };
        })
        .WithName("GetPreferenceSuggestion")
        .WithSummary("根据历史记录学到的画像建议")
        .WithDescription("只建议不应用。用户确认后调用 POST /me/preference/apply-suggestion。")
        .WithTags("画像");

        app.MapPost("/me/preference/apply-suggestion", async (
            HttpContext http,
            PreferenceService preferences,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            var preference = await preferences.ApplySuggestionAsync(userId, ct);

            return ToDto(preference);
        })
        .WithName("ApplyPreferenceSuggestion")
        .WithSummary("应用画像建议")
        .WithTags("画像");

        app.MapGet("/me/summary", async (
            HttpContext http,
            RecordStatsService stats,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);
            return await stats.GetSummaryAsync(userId, ct);
        })
        .WithName("ProfileSummary")
        .WithSummary("「我的」页数据卡片")
        .WithTags("画像");

        return app;
    }

    private static PreferenceDto ToDto(Core.Entities.UserPreference preference) => new()
    {
        SpicyLevel = preference.SpicyLevel,
        BudgetMinCents = preference.BudgetMinCents,
        BudgetMaxCents = preference.BudgetMaxCents,
        AvoidIngredients = [.. preference.AvoidIngredients],
        PreferredCuisines = [.. preference.PreferredCuisines.Select(c => (short)c)],
        DiningModeWeights = new DiningModeWeightsDto
        {
            Takeout = preference.DiningModeWeights.Takeout,
            DineIn = preference.DiningModeWeights.DineIn,
            Homemade = preference.DiningModeWeights.Homemade,
        },
        MaxDistanceMeters = preference.MaxDistanceM,
        OnboardingCompleted = preference.OnboardingCompleted,
        UpdatedAt = preference.UpdatedAt,
    };
}
