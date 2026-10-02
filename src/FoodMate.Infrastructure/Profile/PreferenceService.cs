using FoodMate.Contracts.Profile;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Core.Profile;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodMate.Infrastructure.Profile;

/// <summary>
/// 口味画像服务：读写画像、完成引导、给出学习建议。
/// </summary>
public sealed class PreferenceService(
    FoodMateDbContext db,
    RecordService records,
    IClock clock,
    ILogger<PreferenceService> logger)
{
    /// <summary>忌口项数上限。</summary>
    private const int MaxAvoidIngredients = 20;

    /// <summary>单个忌口词长度上限。</summary>
    private const int MaxAvoidWordLength = 10;

    /// <summary>可接受距离的下限与上限（米）。</summary>
    private const int MinDistanceM = 100;
    private const int MaxDistanceM = 20_000;

    /// <summary>画像学习的时间回溯窗口（天）。</summary>
    private const int LearningLookbackDays = 90;

    private static readonly PreferenceLearningOptions LearningOptions = new();

    /// <summary>取画像；从未设置过时返回默认值并落库。</summary>
    public async Task<UserPreference> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        var preference = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (preference is not null)
        {
            return preference;
        }

        preference = new UserPreference
        {
            UserId = userId,
            UpdatedAt = clock.UtcNow,
        };

        db.UserPreferences.Add(preference);
        await db.SaveChangesAsync(ct);

        return preference;
    }

    /// <summary>更新画像（只改传入的字段）。</summary>
    public async Task<UserPreference> UpdateAsync(
        Guid userId,
        UpdatePreferenceRequest request,
        CancellationToken ct = default)
    {
        var preference = await GetOrCreateAsync(userId, ct);

        if (request.SpicyLevel is { } spicy)
        {
            ValidateSpicy(spicy);
            preference.SpicyLevel = spicy;
        }

        var budgetMin = request.BudgetMinCents ?? preference.BudgetMinCents;
        var budgetMax = request.BudgetMaxCents ?? preference.BudgetMaxCents;
        ValidateBudget(budgetMin, budgetMax);
        preference.BudgetMinCents = budgetMin;
        preference.BudgetMaxCents = budgetMax;

        if (request.AvoidIngredients is { } avoid)
        {
            preference.AvoidIngredients = NormalizeAvoidIngredients(avoid);
        }

        if (request.PreferredCuisines is { } cuisines)
        {
            preference.PreferredCuisines = NormalizeCuisines(cuisines);
        }

        if (request.MaxDistanceMeters is { } distance)
        {
            if (distance is < MinDistanceM or > MaxDistanceM)
            {
                throw BusinessException.Validation(
                    $"可接受距离必须在 {MinDistanceM}–{MaxDistanceM} 米之间。");
            }

            preference.MaxDistanceM = distance;
        }

        preference.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("已更新画像 User={UserId}", userId);

        return preference;
    }

    /// <summary>完成新用户偏好引导。</summary>
    public async Task<UserPreference> CompleteOnboardingAsync(
        Guid userId,
        OnboardingRequest request,
        CancellationToken ct = default)
    {
        ValidateSpicy(request.SpicyLevel);
        ValidateBudget(request.BudgetMinCents, request.BudgetMaxCents);

        var preference = await GetOrCreateAsync(userId, ct);

        preference.SpicyLevel = request.SpicyLevel;
        preference.BudgetMinCents = request.BudgetMinCents;
        preference.BudgetMaxCents = request.BudgetMaxCents;
        preference.AvoidIngredients = NormalizeAvoidIngredients(request.AvoidIngredients);
        preference.PreferredCuisines = NormalizeCuisines(request.PreferredCuisines);

        // 引导时用户只选了「主要就餐方式」，转成偏好权重
        if (Enum.IsDefined(typeof(DiningMode), request.DiningMode)
            && (DiningMode)request.DiningMode != DiningMode.Whatever)
        {
            var mode = (DiningMode)request.DiningMode;
            var weights = new DiningModeWeights { Takeout = 0.2, DineIn = 0.2, Homemade = 0.2 };
            weights.Set(mode, 0.6);
            preference.DiningModeWeights = weights;
        }

        preference.OnboardingCompleted = true;
        preference.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("用户完成偏好引导 User={UserId}", userId);

        return preference;
    }

    /// <summary>
    /// 根据历史记录给出画像建议。
    /// </summary>
    /// <remarks>
    /// <b>只建议，不写入。</b>返回 <c>null</c> 表示样本不足。
    /// </remarks>
    public async Task<PreferenceSuggestion?> SuggestAsync(Guid userId, CancellationToken ct = default)
    {
        var since = clock.UtcNow.AddDays(-LearningLookbackDays);
        var facts = await records.LoadLearningFactsAsync(userId, since, ct);

        return PreferenceUpdater.Suggest(facts, clock.UtcNow, LearningOptions);
    }

    /// <summary>应用画像建议（用户显式确认后调用）。</summary>
    public async Task<UserPreference> ApplySuggestionAsync(Guid userId, CancellationToken ct = default)
    {
        var suggestion = await SuggestAsync(userId, ct)
            ?? throw BusinessException.Validation("记录太少，暂时还看不出你的口味规律。");

        var preference = await GetOrCreateAsync(userId, ct);

        if (suggestion.SpicyLevel is { } spicy)
        {
            preference.SpicyLevel = spicy;
        }

        if (suggestion.BudgetMinCents is { } min && suggestion.BudgetMaxCents is { } max)
        {
            preference.BudgetMinCents = min;
            preference.BudgetMaxCents = max;
        }

        if (suggestion.PreferredCuisines is { Count: > 0 } cuisines)
        {
            preference.PreferredCuisines = [.. cuisines];
        }

        // 就餐方式权重属派生数据，用户不直接编辑，可直接采用
        if (suggestion.DiningModeWeights is { } weights)
        {
            preference.DiningModeWeights = weights;
        }

        preference.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("用户应用了画像建议 User={UserId} 样本={Sample}", userId, suggestion.SampleSize);

        return preference;
    }

    // ── 校验 ────────────────────────────────────────────────

    private static void ValidateSpicy(short spicy)
    {
        if (spicy is < 0 or > 5)
        {
            throw BusinessException.Validation("辣度偏好必须在 0 到 5 之间。");
        }
    }

    private static void ValidateBudget(int min, int max)
    {
        if (min < 0)
        {
            throw BusinessException.Validation("预算下限不能为负。");
        }

        if (max < min)
        {
            throw BusinessException.Validation("预算上限不能低于下限。");
        }
    }

    private static List<string> NormalizeAvoidIngredients(IReadOnlyList<string> raw)
    {
        var cleaned = raw
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count > MaxAvoidIngredients)
        {
            throw BusinessException.Validation($"忌口食材最多 {MaxAvoidIngredients} 项。");
        }

        if (cleaned.Any(x => x.Length > MaxAvoidWordLength))
        {
            throw BusinessException.Validation($"单个忌口词不能超过 {MaxAvoidWordLength} 个字。");
        }

        return cleaned;
    }

    private static List<Cuisine> NormalizeCuisines(IReadOnlyList<short> raw)
    {
        var result = new List<Cuisine>();

        foreach (var value in raw.Distinct())
        {
            if (!Enum.IsDefined(typeof(Cuisine), value))
            {
                throw BusinessException.Validation($"未知的菜系取值：{value}。");
            }

            result.Add((Cuisine)value);
        }

        return result;
    }
}
