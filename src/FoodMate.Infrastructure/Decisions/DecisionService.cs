using FoodMate.Contracts.Decisions;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Decision;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Decisions;

/// <summary>一次决策的完整产出。</summary>
/// <param name="SessionId">决策会话 ID。</param>
/// <param name="Result">引擎结果。</param>
public sealed record DecisionOutcome(Guid SessionId, DecisionResult Result);

/// <summary>用户选择回传的结果。</summary>
/// <param name="SessionId">决策会话 ID。</param>
/// <param name="DishId">选中的菜品。</param>
/// <param name="Rank">该菜品在推荐中的排名；未出现时为 0。</param>
/// <param name="ChosenAt">选择时间。</param>
public sealed record ChooseOutcome(Guid SessionId, Guid DishId, int Rank, DateTimeOffset ChosenAt);

/// <summary>
/// 决策应用服务：负责<b>组装引擎输入</b>与<b>落库</b>，
/// 把纯函数式的 <see cref="IDecisionEngine"/> 与数据库隔开。
/// </summary>
public sealed class DecisionService(
    FoodMateDbContext db,
    IDecisionEngine engine,
    IClock clock,
    IOptions<EngineOptions> engineOptions,
    ILogger<DecisionService> logger)
{
    private const int RecentWindowDays = 30;

    private EngineOptions Options => engineOptions.Value;

    /// <summary>执行一次决策并记录会话。</summary>
    public async Task<DecisionOutcome> SuggestAsync(
        Guid userId,
        DecisionSuggestRequest request,
        CancellationToken ct = default)
    {
        var decisionRequest = ToDomainRequest(request);

        var context = await BuildContextAsync(userId, decisionRequest, ct);

        var result = engine.Decide(context);

        var session = await PersistSessionAsync(userId, decisionRequest, result, ct);

        logger.LogInformation(
            "决策完成 User={UserId} Session={SessionId} 候选={Candidate} 过滤掉={Filtered} 降级={Relaxed} 耗时={Elapsed}ms",
            userId, session.Id, result.CandidateCount, result.FilteredOutCount,
            result.RelaxedLevel, result.ElapsedMs);

        return new DecisionOutcome(session.Id, result);
    }

    /// <summary>记录用户最终选择。</summary>
    /// <remarks>
    /// 这是<b>权重调优的核心数据来源</b>——即使用户没建记录，也应调用。
    /// </remarks>
    public async Task<ChooseOutcome> ChooseAsync(
        Guid userId,
        Guid sessionId,
        Guid dishId,
        string source,
        CancellationToken ct = default)
    {
        var session = await db.DecisionSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct)
            ?? throw BusinessException.NotFound($"决策会话 {sessionId} 不存在");

        var index = session.Candidates.FindIndex(c => c.DishId == dishId);
        var rank = index >= 0 ? index + 1 : 0;

        session.ChosenDishId = dishId;
        session.ChosenAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "用户选择 Session={SessionId} Dish={DishId} Rank={Rank} Source={Source}",
            sessionId, dishId, rank, source);

        return new ChooseOutcome(sessionId, dishId, rank, session.ChosenAt.Value);
    }

    // ── 输入组装 ────────────────────────────────────────────

    private DecisionRequest ToDomainRequest(DecisionSuggestRequest request)
    {
        MealType? mealType = request.MealType is { } raw && Enum.IsDefined(typeof(MealType), raw)
            ? (MealType)raw
            : null;

        var diningMode = Enum.IsDefined(typeof(DiningMode), request.DiningMode)
            ? (DiningMode)request.DiningMode
            : DiningMode.Whatever;

        return new DecisionRequest
        {
            MealType = mealType,
            DiningMode = diningMode,
            PartySize = request.PartySize <= 0 ? (short)1 : request.PartySize,
            BudgetMinCents = request.BudgetMinCents,
            BudgetMaxCents = request.BudgetMaxCents,
            Location = request.Location is { } point
                ? new GeoPoint(point.Latitude, point.Longitude)
                : null,
            Weather = request.Weather,
            MoodTags = [.. request.MoodTags.Take(3)],
            ExcludeDishIds = request.ExcludeDishIds,
            ExploreJitter = Math.Max(0, request.ExploreJitter),
            Now = clock.UtcNow,
        };
    }

    private async Task<DecisionContext> BuildContextAsync(
        Guid userId,
        DecisionRequest request,
        CancellationToken ct)
    {
        var candidates = await LoadCandidatesAsync(userId, ct);
        var preference = await LoadPreferenceAsync(userId, ct);
        var (dishStats, cuisineStats) = await LoadStatsAsync(userId, request.Now, ct);

        var recordCount = await db.MealRecords
            .AsNoTracking()
            .CountAsync(r => r.UserId == userId && !r.IsDeleted, ct);

        return new DecisionContext
        {
            Request = request,
            Preference = preference,
            Candidates = candidates,
            DishStats = dishStats,
            CuisineStats = cuisineStats,
            UserRecordCount = recordCount,
            Options = Options,
        };
    }

    private async Task<IReadOnlyList<DishCandidate>> LoadCandidatesAsync(Guid userId, CancellationToken ct)
    {
        var dishes = await db.Dishes
            .AsNoTracking()
            .Include(d => d.Recipe)
            .Where(d => d.IsActive && !d.IsDeleted && (d.IsBuiltin || d.OwnerUserId == userId))
            .ToListAsync(ct);

        return
        [
            .. dishes.Select(d => new DishCandidate
            {
                Id = d.Id,
                Name = d.Name,
                Cuisine = d.Cuisine,
                Category = d.Category,
                SpicyLevel = d.SpicyLevel,
                PriceMinCents = d.PriceMinCents,
                PriceMaxCents = d.PriceMaxCents,
                Calories = d.Calories,
                Ingredients = [.. d.Ingredients],
                Tags = [.. d.Tags],
                MealTimes = d.MealTimes,
                Seasons = d.Seasons,
                HasRecipe = d.Recipe is not null,
                CookMinutes = d.Recipe?.CookMinutes,
                Popularity = d.Popularity,
                IsBuiltin = d.IsBuiltin,
                // v1 无商家/门店数据，距离维度保持未知（引擎按中性处理）。
                // 接入门店数据后在此计算。
                DistanceKm = null,
            }),
        ];
    }

    private async Task<UserPreferenceSnapshot> LoadPreferenceAsync(Guid userId, CancellationToken ct)
    {
        var preference = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (preference is null)
        {
            return UserPreferenceSnapshot.Default;
        }

        return new UserPreferenceSnapshot
        {
            SpicyLevel = preference.SpicyLevel,
            BudgetMinCents = preference.BudgetMinCents,
            BudgetMaxCents = preference.BudgetMaxCents,
            AvoidIngredients = [.. preference.AvoidIngredients],
            PreferredCuisines = [.. preference.PreferredCuisines],
            DiningModeWeights = preference.DiningModeWeights,
            MaxDistanceM = preference.MaxDistanceM,
        };
    }

    /// <summary>
    /// 加载用户历史统计，并顺带算出近 30 天的菜品级与菜系级频次。
    /// </summary>
    /// <remarks>
    /// 近 30 天频次不在统计表里，需要从 <c>meal_records</c> 现算。
    /// 这里刻意拆成<b>两个简单查询</b>而不是一个 <c>Join</c>：
    /// <c>meal_records.dish_id</c> 是可空外键，与 <c>dishes.id</c> 做连接时
    /// 会产生 <c>Guid?</c> → <c>Guid</c> 的转换节点，EF Core 无法翻译。
    /// 拆开之后两个查询都能翻译，且在内存聚合的成本可以忽略。
    /// </remarks>
    private async Task<(
        IReadOnlyDictionary<Guid, DishStatSnapshot> DishStats,
        IReadOnlyDictionary<Cuisine, CuisineStatSnapshot> CuisineStats)>
        LoadStatsAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-RecentWindowDays);

        // ① 近 30 天吃过哪些菜、各吃了几次
        var recentRecords = await db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && !r.IsDeleted
                        && r.DishId != null
                        && r.EatenAt >= since)
            .Select(r => r.DishId)
            .ToListAsync(ct);

        var dishRecent = recentRecords
            .Where(id => id is not null)
            .GroupBy(id => id!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // ② 这些菜分别属于哪个菜系
        var recentDishIds = dishRecent.Keys.ToList();

        var cuisineOfDish = recentDishIds.Count == 0
            ? []
            : await db.Dishes
                .AsNoTracking()
                .Where(d => recentDishIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Cuisine })
                .ToDictionaryAsync(d => d.Id, d => d.Cuisine, ct);

        var cuisineRecent = new Dictionary<Cuisine, int>();
        foreach (var (dishId, count) in dishRecent)
        {
            if (!cuisineOfDish.TryGetValue(dishId, out var cuisine))
            {
                continue;
            }

            cuisineRecent[cuisine] = cuisineRecent.GetValueOrDefault(cuisine) + count;
        }

        // ③ 用户 × 菜品统计
        var dishStatRows = await db.UserDishStats
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var dishStats = dishStatRows.ToDictionary(
            s => s.DishId,
            s => new DishStatSnapshot(
                EatCount: s.EatCount,
                EatCount30d: dishRecent.GetValueOrDefault(s.DishId),
                LastEatenAt: s.LastEatenAt,
                RatingSum: s.RatingSum,
                RatingCount: s.RatingCount,
                WouldEatAgainCount: s.WouldEatAgainCount,
                WouldNotEatAgainCount: s.WouldNotEatAgainCount));

        // ④ 用户 × 菜系统计
        var cuisineStatRows = await db.UserCuisineStats
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

        var cuisineStats = cuisineStatRows.ToDictionary(
            s => s.Cuisine,
            s => new CuisineStatSnapshot(
                EatCountTotal: s.EatCountTotal,
                EatCount30d: cuisineRecent.GetValueOrDefault(s.Cuisine),
                LastEatenAt: s.LastEatenAt));

        return (dishStats, cuisineStats);
    }

    private async Task<DecisionSession> PersistSessionAsync(
        Guid userId,
        DecisionRequest request,
        DecisionResult result,
        CancellationToken ct)
    {
        // 在初始化器外先解构，避免在同一作用域里重复声明模式变量
        var location = request.Location;

        var session = new DecisionSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MealType = request.ResolveMealType(),
            DiningMode = request.DiningMode,
            PartySize = request.PartySize,
            BudgetMinCents = request.BudgetMinCents,
            BudgetMaxCents = request.BudgetMaxCents,
            Latitude = location is null ? null : (decimal)location.Latitude,
            Longitude = location is null ? null : (decimal)location.Longitude,
            Weather = request.Weather,
            MoodTags = [.. request.MoodTags],
            CandidateCount = result.CandidateCount,
            Candidates =
            [
                .. result.Ranked.Select(item => new DecisionCandidateSnapshot
                {
                    DishId = item.DishId,
                    Name = item.Name,
                    TotalScore = Math.Round(item.Score, 2),
                    Breakdown = item.Breakdown.ToDictionary(
                        pair => ScoreDimensionKeys.Key(pair.Key),
                        pair => Math.Round(pair.Value, 4)),
                    Penalties = [.. item.Penalties.Select(p => p.Code)],
                    Reasons = [.. item.Reasons],
                }),
            ],
            EngineVersion = result.EngineVersion,
            ElapsedMs = (int)result.ElapsedMs,
            CreatedAt = clock.UtcNow,
        };

        db.DecisionSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return session;
    }
}
