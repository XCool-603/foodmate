using FoodMate.Contracts.Decisions;
using FoodMate.Contracts.Dishes;
using FoodMate.Core.Decision;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Decisions;
using FoodMate.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FoodMate.Api.Endpoints;

/// <summary>
/// 决策端点 —— 本产品的核心接口。
/// </summary>
public static class DecisionEndpoints
{
    public static IEndpointRouteBuilder MapDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/decisions/suggest", async (
            DecisionSuggestRequest request,
            HttpContext http,
            DecisionService decisions,
            GuestUserService users,
            IOptions<EngineOptions> options,
            CancellationToken ct) =>
        {
            var userId = await ResolveUserIdAsync(http, users, ct);
            var outcome = await decisions.SuggestAsync(userId, request, ct);

            return MapToResponse(outcome, options.Value.TopN.Wheel);
        })
        .WithName("SuggestDecision")
        .WithSummary("获取「今天吃什么」推荐")
        .WithDescription(
            "对候选菜品执行硬过滤 → 七维加权打分 → 乘性惩罚 → 排序，"
            + "返回 Top N 及每条推荐的人话理由。")
        .WithTags("决策");

        app.MapPost("/decisions/{sessionId:guid}/choose", async (
            Guid sessionId,
            DecisionChooseRequest request,
            HttpContext http,
            DecisionService decisions,
            GuestUserService users,
            CancellationToken ct) =>
        {
            if (request.DishId == Guid.Empty)
            {
                throw BusinessException.Validation("dishId 不能为空。");
            }

            var userId = await ResolveUserIdAsync(http, users, ct);
            var outcome = await decisions.ChooseAsync(userId, sessionId, request.DishId, request.Source, ct);

            return new DecisionChooseResponse
            {
                SessionId = outcome.SessionId,
                DishId = outcome.DishId,
                Rank = outcome.Rank,
                ChosenAt = outcome.ChosenAt,
            };
        })
        .WithName("ChooseDecision")
        .WithSummary("回传用户最终选择")
        .WithDescription("记录用户从推荐中选了哪道菜及其排名，是决策权重调优的核心数据来源。")
        .WithTags("决策");

        return app;
    }

    // ── 映射 ────────────────────────────────────────────────
    private static DecisionSuggestResponse MapToResponse(DecisionOutcome outcome, int wheelSize)
    {
        var result = outcome.Result;

        var ranked = result.Ranked
            .Select((item, index) => new ScoredDishDto
            {
                Rank = index + 1,
                DishId = item.DishId,
                Name = item.Name,
                Score = Math.Round(item.Score, 1),
                Breakdown = item.Breakdown.ToDictionary(
                    pair => ScoreDimensionKeys.Key(pair.Key),
                    pair => Math.Round(pair.Value, 4)),
                Penalties =
                [
                    .. item.Penalties.Select(p => new PenaltyDto(p.Code, p.Multiplier, p.Description)),
                ],
                Reasons = [.. item.Reasons],
                Dish = DishBriefDto.FromCandidate(item.Dish),
            })
            .ToList();

        return new DecisionSuggestResponse
        {
            SessionId = outcome.SessionId,
            EngineVersion = result.EngineVersion,
            ElapsedMs = result.ElapsedMs,
            CandidateCount = result.CandidateCount,
            FilteredOutCount = result.FilteredOutCount,
            RelaxedLevel = result.RelaxedLevel,
            WheelPicks = [.. ranked.Take(wheelSize).Select(item => item.DishId)],
            Ranked = ranked,
        };
    }

    // ── 身份解析（M4 接入真实登录后替换）────────────────────

    private static Task<Guid> ResolveUserIdAsync(
        HttpContext http,
        GuestUserService users,
        CancellationToken ct)
        => CurrentUser.ResolveAsync(http, users, ct);
}
