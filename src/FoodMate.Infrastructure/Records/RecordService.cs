using FoodMate.Contracts.Records;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Decision;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Core.Profile;
using FoodMate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodMate.Infrastructure.Records;

/// <summary>
/// 饮食记录服务：CRUD + <b>统计表维护</b> + 报表。
/// </summary>
/// <remarks>
/// <para>
/// 记录是留存闭环的核心：它同时是「我吃了什么」的事实，
/// 也是决策引擎 <c>S_fresh</c> / <c>S_affinity</c> 的唯一数据来源。
/// </para>
/// <para>
/// <b>统计表维护策略：重算而非增量。</b>
/// 每次增删改后，只对<b>受影响的那一行</b>（该用户 × 该菜品、该用户 × 该菜系）
/// 从 <c>meal_records</c> 重新聚合。相比加减法，重算不会因为漏减、重复减、
/// 浮点误差而积累漂移，代价只是一次单行的小查询。
/// </para>
/// </remarks>
public sealed class RecordService(
    FoodMateDbContext db,
    IClock clock,
    ILogger<RecordService> logger)
{
    /// <summary>份量系数的合法上限。</summary>
    private const double MaxServings = 10.0;

    /// <summary>备注长度上限。</summary>
    private const int MaxNoteLength = 200;

    /// <summary>待评分提醒的默认回溯天数。</summary>
    private const int PendingRatingWindowDays = 7;

    /// <summary>记录时间允许的最大未来偏移。</summary>
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromHours(1);

    /// <summary>记录时间允许的最大回溯。</summary>
    private static readonly TimeSpan MaxPastSkew = TimeSpan.FromDays(365);

    /// <summary>
    /// 计算「一天」时使用的本地时区偏移。
    /// </summary>
    /// <remarks>
    /// 中国全境使用 UTC+8 且无夏令时，因此先用常量。
    /// M4 接入登录后改为按用户记录。
    /// </remarks>
    public static readonly TimeSpan DefaultLocalOffset = TimeSpan.FromHours(8);

    // ── 写操作 ──────────────────────────────────────────────

    /// <summary>创建记录。</summary>
    public async Task<MealRecord> CreateAsync(
        Guid userId,
        CreateRecordRequest request,
        CancellationToken ct = default)
    {
        var dish = await ResolveDishAsync(userId, request.DishId, ct);

        var dishName = dish?.Name ?? request.DishName?.Trim();

        if (string.IsNullOrWhiteSpace(dishName))
        {
            throw BusinessException.Validation("必须提供 dishId 或 dishName。");
        }

        if (dishName.Length > 60)
        {
            throw BusinessException.Validation("菜名不能超过 60 个字。");
        }

        ValidateServings(request.Servings);
        ValidateRating(request.Rating);

        if (request.Note is { Length: > MaxNoteLength })
        {
            throw BusinessException.Validation($"备注不能超过 {MaxNoteLength} 个字。");
        }

        var eatenAt = NormalizeEatenAt(request.EatenAt);

        // 忌口冲突：记录是客观事实，不阻止，但要用户确认一次
        if (dish is not null && !request.Force)
        {
            await EnsureNoAvoidConflictAsync(userId, dish, ct);
        }

        var mealType = ResolveMealType(request.MealType, eatenAt);
        var diningMode = ResolveDiningMode(request.DiningMode);

        var record = new MealRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DishId = dish?.Id,
            DishName = dishName,
            DishSnapshot = dish is not null ? DishSnapshot.From(dish) : new DishSnapshot(),
            MealType = mealType,
            DiningMode = diningMode,
            EatenAt = eatenAt,
            Servings = request.Servings,
            Calories = EstimateCalories(dish, request.Servings),
            Rating = request.Rating,
            WouldEatAgain = request.WouldEatAgain,
            PhotoUrl = request.PhotoUrl,
            Note = request.Note,
            Source = Enum.IsDefined(typeof(RecordSource), request.Source)
                ? (RecordSource)request.Source
                : RecordSource.Manual,
            DecisionSessionId = request.DecisionSessionId,
            IsDeleted = false,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
        };

        db.MealRecords.Add(record);
        await db.SaveChangesAsync(ct);

        await RecomputeStatsAsync(userId, dish, ct);

        logger.LogInformation(
            "已创建记录 {RecordId} User={UserId} Dish={DishName}",
            record.Id, userId, dishName);

        return record;
    }

    /// <summary>更新记录。</summary>
    public async Task<MealRecord> UpdateAsync(
        Guid userId,
        Guid recordId,
        UpdateRecordRequest request,
        CancellationToken ct = default)
    {
        var record = await db.MealRecords
            .FirstOrDefaultAsync(r => r.Id == recordId && r.UserId == userId && !r.IsDeleted, ct)
            ?? throw BusinessException.NotFound($"记录 {recordId} 不存在");

        var dish = record.DishId is { } dishId
            ? await db.Dishes.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dishId, ct)
            : null;

        if (request.Servings is { } servings)
        {
            ValidateServings(servings);
            record.Servings = servings;
            record.Calories = EstimateCalories(dish, servings);
        }

        if (request.Rating is { } rating)
        {
            ValidateRating(rating);
            record.Rating = rating;
        }

        if (request.WouldEatAgain is { } wouldEatAgain)
        {
            record.WouldEatAgain = wouldEatAgain;
        }

        if (request.MealType is { } mealType && Enum.IsDefined(typeof(MealType), mealType))
        {
            record.MealType = (MealType)mealType;
        }

        if (request.DiningMode is { } diningMode && Enum.IsDefined(typeof(DiningMode), diningMode))
        {
            record.DiningMode = (DiningMode)diningMode;
        }

        if (request.EatenAt is { } eatenAt)
        {
            record.EatenAt = NormalizeEatenAt(eatenAt);
        }

        if (request.Note is not null)
        {
            if (request.Note.Length > MaxNoteLength)
            {
                throw BusinessException.Validation($"备注不能超过 {MaxNoteLength} 个字。");
            }

            record.Note = request.Note;
        }

        if (request.PhotoUrl is not null)
        {
            record.PhotoUrl = request.PhotoUrl;
        }

        record.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        await RecomputeStatsAsync(userId, dish, ct);

        return record;
    }

    /// <summary>快速评分。</summary>
    public async Task<MealRecord> RateAsync(
        Guid userId,
        Guid recordId,
        short rating,
        bool? wouldEatAgain,
        CancellationToken ct = default)
    {
        ValidateRating(rating);

        var record = await db.MealRecords
            .FirstOrDefaultAsync(r => r.Id == recordId && r.UserId == userId && !r.IsDeleted, ct)
            ?? throw BusinessException.NotFound($"记录 {recordId} 不存在");

        record.Rating = rating;

        if (wouldEatAgain is { } value)
        {
            record.WouldEatAgain = value;
        }

        record.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        var dish = record.DishId is { } dishId
            ? await db.Dishes.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dishId, ct)
            : null;

        await RecomputeStatsAsync(userId, dish, ct);

        return record;
    }

    /// <summary>删除记录（软删除）。</summary>
    public async Task DeleteAsync(Guid userId, Guid recordId, CancellationToken ct = default)
    {
        var record = await db.MealRecords
            .FirstOrDefaultAsync(r => r.Id == recordId && r.UserId == userId && !r.IsDeleted, ct)
            ?? throw BusinessException.NotFound($"记录 {recordId} 不存在");

        var dish = record.DishId is { } dishId
            ? await db.Dishes.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dishId, ct)
            : null;

        record.IsDeleted = true;
        record.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
        await RecomputeStatsAsync(userId, dish, ct);

        logger.LogInformation("已删除记录 {RecordId} User={UserId}", recordId, userId);
    }

    // ── 读操作 ──────────────────────────────────────────────

    /// <summary>取单条记录。</summary>
    public async Task<MealRecord> GetAsync(Guid userId, Guid recordId, CancellationToken ct = default)
        => await db.MealRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recordId && r.UserId == userId && !r.IsDeleted, ct)
            ?? throw BusinessException.NotFound($"记录 {recordId} 不存在");

    /// <summary>分页查询记录，按就餐时间倒序。</summary>
    public async Task<PagedResult<MealRecord>> ListAsync(
        Guid userId,
        RecordQuery query,
        CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var source = db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsDeleted);

        if (query.From is { } from)
        {
            source = source.Where(r => r.EatenAt >= ToUtcStart(from));
        }

        if (query.To is { } to)
        {
            source = source.Where(r => r.EatenAt < ToUtcStart(to.AddDays(1)));
        }

        if (query.MealType is { } mealType)
        {
            source = source.Where(r => (short)r.MealType == mealType);
        }

        if (query.DishId is { } dishId)
        {
            source = source.Where(r => r.DishId == dishId);
        }

        if (query.HasRating is { } hasRating)
        {
            source = hasRating
                ? source.Where(r => r.Rating != null)
                : source.Where(r => r.Rating == null);
        }

        var total = await source.CountAsync(ct);

        var items = await source
            .OrderByDescending(r => r.EatenAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<MealRecord>.Create(items, total, page, pageSize);
    }

    /// <summary>取最近若干天未评分的记录，用于主动提醒。</summary>
    public async Task<IReadOnlyList<MealRecord>> PendingRatingAsync(
        Guid userId,
        int limit = 3,
        CancellationToken ct = default)
    {
        var since = clock.UtcNow.AddDays(-PendingRatingWindowDays);

        return await db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && !r.IsDeleted
                        && r.Rating == null
                        && r.EatenAt >= since)
            .OrderByDescending(r => r.EatenAt)
            .Take(Math.Clamp(limit, 1, 20))
            .ToListAsync(ct);
    }

    /// <summary>取用户全部历史，供画像学习使用。</summary>
    public async Task<IReadOnlyList<LearnedRecordFact>> LoadLearningFactsAsync(
        Guid userId,
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        var rows = await db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsDeleted && r.EatenAt >= since)
            .Select(r => new
            {
                r.DishSnapshot,
                r.DiningMode,
                r.Rating,
                r.EatenAt,
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(r => new LearnedRecordFact(
                Cuisine: r.DishSnapshot.Cuisine,
                SpicyLevel: r.DishSnapshot.SpicyLevel,
                PriceCents: r.DishSnapshot.PriceCents,
                DiningMode: r.DiningMode,
                Rating: r.Rating,
                EatenAt: r.EatenAt)),
        ];
    }

    // ── 统计表维护 ──────────────────────────────────────────

    private async Task RecomputeStatsAsync(Guid userId, Dish? dish, CancellationToken ct)
    {
        if (dish is not null)
        {
            await RecomputeDishStatAsync(userId, dish.Id, ct);
            await RecomputeCuisineStatAsync(userId, dish.Cuisine, ct);
        }
    }

    /// <summary>重算「用户 × 菜品」统计行。</summary>
    private async Task RecomputeDishStatAsync(Guid userId, Guid dishId, CancellationToken ct)
    {
        var rows = await db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.DishId == dishId && !r.IsDeleted)
            .Select(r => new { r.Servings, r.EatenAt, r.Rating, r.WouldEatAgain })
            .ToListAsync(ct);

        var stat = await db.UserDishStats
            .FirstOrDefaultAsync(s => s.UserId == userId && s.DishId == dishId, ct);

        if (rows.Count == 0)
        {
            if (stat is not null)
            {
                db.UserDishStats.Remove(stat);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        stat ??= new UserDishStat { UserId = userId, DishId = dishId };

        if (db.Entry(stat).State == EntityState.Detached)
        {
            db.UserDishStats.Add(stat);
        }

        stat.EatCount = (int)Math.Round(rows.Sum(r => r.Servings));
        stat.LastEatenAt = rows.Max(r => r.EatenAt);
        stat.RatingSum = rows.Where(r => r.Rating.HasValue).Sum(r => (int)r.Rating!.Value);
        stat.RatingCount = rows.Count(r => r.Rating.HasValue);
        stat.WouldEatAgainCount = rows.Count(r => r.WouldEatAgain == true);
        stat.WouldNotEatAgainCount = rows.Count(r => r.WouldEatAgain == false);
        stat.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>重算「用户 × 菜系」统计行。</summary>
    private async Task RecomputeCuisineStatAsync(Guid userId, Cuisine cuisine, CancellationToken ct)
    {
        // 菜系信息存在菜品表里，先用菜品 ID 集合筛出候选记录，再在内存里聚合
        var dishIds = await db.Dishes
            .AsNoTracking()
            .Where(d => d.Cuisine == cuisine)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var rows = dishIds.Count == 0
            ? []
            : await db.MealRecords
                .AsNoTracking()
                .Where(r => r.UserId == userId && !r.IsDeleted && r.DishId != null
                            && dishIds.Contains(r.DishId!.Value))
                .Select(r => new { r.Servings, r.EatenAt })
                .ToListAsync(ct);

        var stat = await db.UserCuisineStats
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Cuisine == cuisine, ct);

        if (rows.Count == 0)
        {
            if (stat is not null)
            {
                db.UserCuisineStats.Remove(stat);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        stat ??= new UserCuisineStat { UserId = userId, Cuisine = cuisine };

        if (db.Entry(stat).State == EntityState.Detached)
        {
            db.UserCuisineStats.Add(stat);
        }

        stat.EatCountTotal = (int)Math.Round(rows.Sum(r => r.Servings));
        stat.LastEatenAt = rows.Max(r => r.EatenAt);
        stat.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    // ── 校验与换算 ──────────────────────────────────────────

    private async Task<Dish?> ResolveDishAsync(Guid userId, Guid? dishId, CancellationToken ct)
    {
        if (dishId is not { } id)
        {
            return null;
        }

        var dish = await db.Dishes
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct)
            ?? throw BusinessException.NotFound($"菜品 {id} 不存在");

        if (!dish.IsBuiltin && dish.OwnerUserId != userId)
        {
            throw BusinessException.Forbidden("无权使用他人的自定义菜品。");
        }

        return dish;
    }

    private async Task EnsureNoAvoidConflictAsync(Guid userId, Dish dish, CancellationToken ct)
    {
        var preference = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (preference is null || preference.AvoidIngredients.Count == 0)
        {
            return;
        }

        var expanded = HardFilter.ExpandAvoidList(preference.AvoidIngredients);

        var candidate = new DishCandidate
        {
            Id = dish.Id,
            Name = dish.Name,
            Cuisine = dish.Cuisine,
            Category = dish.Category,
            SpicyLevel = dish.SpicyLevel,
            Ingredients = [.. dish.Ingredients],
        };

        if (!HardFilter.ContainsAvoided(candidate, expanded))
        {
            return;
        }

        var hits = dish.Ingredients
            .Where(i => expanded.Any(a =>
                i.Name.Contains(a, StringComparison.OrdinalIgnoreCase)
                || (i.Name.Length >= 2 && a.Contains(i.Name, StringComparison.OrdinalIgnoreCase))))
            .Select(i => i.Name)
            .Distinct()
            .ToList();

        throw new BusinessException(
            ApiErrorCode.AvoidIngredientConflict,
            $"「{dish.Name}」含你忌口的食材：{string.Join("、", hits)}。确认要记录吗？");
    }

    private DateTimeOffset NormalizeEatenAt(DateTimeOffset? requested)
    {
        var eatenAt = (requested ?? clock.UtcNow).ToUniversalTime();

        if (eatenAt > clock.UtcNow.Add(MaxFutureSkew))
        {
            throw BusinessException.Validation("就餐时间不能晚于当前时间。");
        }

        if (eatenAt < clock.UtcNow.Subtract(MaxPastSkew))
        {
            throw BusinessException.Validation("就餐时间不能早于一年前。");
        }

        return eatenAt;
    }

    private static void ValidateServings(double servings)
    {
        if (servings <= 0 || servings > MaxServings)
        {
            throw BusinessException.Validation($"份量系数必须在 0 到 {MaxServings} 之间。");
        }
    }

    private static void ValidateRating(short? rating)
    {
        if (rating is { } value && (value < 1 || value > 5))
        {
            throw BusinessException.Validation("评分必须在 1 到 5 之间。");
        }
    }

    private static MealType ResolveMealType(short raw, DateTimeOffset eatenAt)
        => Enum.IsDefined(typeof(MealType), raw)
            ? (MealType)raw
            : MealTimes.InferFromUtc(eatenAt, DefaultLocalOffset);

    private static DiningMode ResolveDiningMode(short raw)
        => Enum.IsDefined(typeof(DiningMode), raw)
            ? (DiningMode)raw
            : DiningMode.Whatever;

    private static int? EstimateCalories(Dish? dish, double servings)
        => dish?.Calories is { } perServing
            ? (int)Math.Round(perServing * servings)
            : null;

    /// <summary>把本地日期转成 UTC 起点。</summary>
    private static DateTimeOffset ToUtcStart(DateOnly localDate)
        => new DateTimeOffset(localDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            .Subtract(DefaultLocalOffset);

    /// <summary>把 UTC 时刻转成本地日期。</summary>
    public static DateOnly ToLocalDate(DateTimeOffset utc)
        => DateOnly.FromDateTime(utc.ToOffset(DefaultLocalOffset).DateTime);
}
