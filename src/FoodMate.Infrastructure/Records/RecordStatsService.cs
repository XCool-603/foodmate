using FoodMate.Contracts.Records;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Enums;
using FoodMate.Core.Profile;
using FoodMate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Infrastructure.Records;

/// <summary>统计报表与「我的」页数据卡片。</summary>
/// <remarks>
/// 聚合全部在内存中完成。单个用户的记录量级是每年数百条，
/// 一次性取回远比让 EF 翻译复杂的 <c>GroupBy</c> 更稳妥，
/// 也不依赖数据库特有的日期函数。
/// </remarks>
public sealed class RecordStatsService(
    FoodMateDbContext db,
    IClock clock)
{
    /// <summary>统计报表的默认区间长度（天）。</summary>
    private const int DefaultRangeDays = 7;

    /// <summary>生成统计报表。</summary>
    public async Task<RecordStatsDto> GetStatsAsync(
        Guid userId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var today = RecordService.ToLocalDate(clock.UtcNow);
        var end = to ?? today;
        var start = from ?? end.AddDays(-(DefaultRangeDays - 1));

        if (start > end)
        {
            (start, end) = (end, start);
        }

        var fromUtc = ToUtcStart(start);
        var toUtc = ToUtcStart(end.AddDays(1));

        var rows = await db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsDeleted
                        && r.EatenAt >= fromUtc && r.EatenAt < toUtc)
            .Select(r => new
            {
                r.DishId,
                r.DishName,
                r.DishSnapshot,
                r.MealType,
                r.DiningMode,
                r.EatenAt,
                r.Calories,
                r.Rating,
            })
            .ToListAsync(ct);

        var totalCalories = rows.Sum(r => r.Calories ?? 0);
        var rated = rows.Where(r => r.Rating.HasValue).Select(r => (double)r.Rating!.Value).ToList();

        // 该区间内「首次出现」的菜品：需要与区间之前的记录比对
        var newDishesTried = await CountNewDishesAsync(userId, rows.Select(r => r.DishId).Distinct().ToList(), fromUtc, ct);

        return new RecordStatsDto
        {
            From = start,
            To = end,
            TotalRecords = rows.Count,
            TotalCalories = totalCalories,
            AverageCaloriesPerMeal = rows.Count == 0 ? 0 : totalCalories / rows.Count,
            AverageRating = rated.Count == 0 ? null : Math.Round(rated.Average(), 2),
            RecordedDays = rows.Select(r => RecordService.ToLocalDate(r.EatenAt)).Distinct().Count(),
            NewDishesTried = newDishesTried,
            MealTypeDistribution = BuildDistribution(
                rows.GroupBy(r => (short)r.MealType).Select(g => (g.Key, g.Count())),
                v => EnumLabels.MealType((MealType)v)),
            CuisineDistribution = BuildDistribution(
                rows.GroupBy(r => (short)r.DishSnapshot.Cuisine).Select(g => (g.Key, g.Count())),
                v => EnumLabels.Cuisine((Cuisine)v)),
            DiningModeDistribution = BuildDistribution(
                rows.GroupBy(r => (short)r.DiningMode).Select(g => (g.Key, g.Count())),
                v => EnumLabels.DiningMode((DiningMode)v)),
            DailyCalories =
            [
                .. rows
                    .GroupBy(r => RecordService.ToLocalDate(r.EatenAt))
                    .OrderBy(g => g.Key)
                    .Select(g => new DailyCaloriesDto(g.Key, g.Sum(r => r.Calories ?? 0))),
            ],
        };
    }

    /// <summary>「我的」页数据卡片。</summary>
    public async Task<ProfileSummaryDto> GetSummaryAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsDeleted)
            .Select(r => new
            {
                r.DishId,
                r.DishSnapshot,
                r.EatenAt,
                r.Rating,
            })
            .ToListAsync(ct);

        var today = RecordService.ToLocalDate(clock.UtcNow);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));   // 本周一

        var localDates = rows
            .Select(r => RecordService.ToLocalDate(r.EatenAt))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var (currentStreak, longestStreak) = StreakCalculator.Compute(localDates, today);

        var thisWeek = rows
            .Where(r => RecordService.ToLocalDate(r.EatenAt) >= weekStart)
            .ToList();

        // 本周首次出现 = 本周有记录、且本周之前没有
        var thisWeekDishIds = thisWeek
            .Where(r => r.DishId != null)
            .Select(r => r.DishId!.Value)
            .Distinct()
            .ToList();

        var seenBefore = thisWeekDishIds.Count == 0
            ? []
            : await db.MealRecords
                .AsNoTracking()
                .Where(r => r.UserId == userId && !r.IsDeleted
                            && r.DishId != null
                            && thisWeekDishIds.Contains(r.DishId!.Value)
                            && r.EatenAt < ToUtcStart(weekStart))
                .Select(r => r.DishId!.Value)
                .Distinct()
                .ToListAsync(ct);

        var rated = rows.Where(r => r.Rating.HasValue).Select(r => (double)r.Rating!.Value).ToList();

        var favorite = rows
            .Where(r => r.DishSnapshot.Cuisine != Cuisine.Other)
            .GroupBy(r => r.DishSnapshot.Cuisine)
            .Select(g => new { Cuisine = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Cuisine)
            .FirstOrDefault();

        return new ProfileSummaryDto
        {
            TotalRecords = rows.Count,
            TotalDishesTried = rows.Where(r => r.DishId != null).Select(r => r.DishId).Distinct().Count(),
            CurrentStreakDays = currentStreak,
            LongestStreakDays = longestStreak,
            ThisWeekRecords = thisWeek.Count,
            ThisWeekNewDishes = thisWeekDishIds.Except(seenBefore).Count(),
            FavoriteCuisine = favorite is null
                ? null
                : new DistributionItemDto(
                    (short)favorite.Cuisine,
                    EnumLabels.Cuisine(favorite.Cuisine),
                    favorite.Count),
            AverageRating = rated.Count == 0 ? null : Math.Round(rated.Average(), 2),
        };
    }

    /// <summary>
    /// 计算连续打卡天数。
    /// </summary>
    /// <remarks>
    /// 「当前连续」允许以今天或昨天结尾——今天还没吃饭不该算断签。
    /// </remarks>
    public static (int Current, int Longest) ComputeStreaks(
        IReadOnlyList<DateOnly> sortedDistinctDates,
        DateOnly today)
        => StreakCalculator.Compute(sortedDistinctDates, today);

    private async Task<int> CountNewDishesAsync(
        Guid userId,
        IReadOnlyList<Guid?> dishIds,
        DateTimeOffset rangeStart,
        CancellationToken ct)
    {
        var ids = dishIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        if (ids.Count == 0)
        {
            return 0;
        }

        var seenBefore = await db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId && !r.IsDeleted
                        && r.DishId != null
                        && ids.Contains(r.DishId!.Value)
                        && r.EatenAt < rangeStart)
            .Select(r => r.DishId!.Value)
            .Distinct()
            .ToListAsync(ct);

        return ids.Except(seenBefore).Count();
    }

    private static List<DistributionItemDto> BuildDistribution(
        IEnumerable<(short Value, int Count)> source,
        Func<short, string> labelOf)
        => [.. source
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Value)
            .Select(x => new DistributionItemDto(x.Value, LabelOrUnknown(labelOf, x.Value), x.Count))];

    /// <summary>枚举值可能来自历史数据，越界时不应抛异常。</summary>
    private static string LabelOrUnknown(Func<short, string> labelOf, short value)
    {
        try
        {
            return labelOf(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "未知";
        }
    }

    private static DateTimeOffset ToUtcStart(DateOnly localDate)
        => new DateTimeOffset(localDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            .Subtract(RecordService.DefaultLocalOffset);
}
