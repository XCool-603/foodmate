namespace FoodMate.Core.Profile;

/// <summary>
/// 连续打卡天数计算。
/// </summary>
/// <remarks>
/// <b>纯函数</b>，不依赖数据库与时钟——日期由调用方传入。
/// </remarks>
public static class StreakCalculator
{
    /// <summary>
    /// 计算当前与最长的连续打卡天数。
    /// </summary>
    /// <param name="sortedDistinctDates">已去重并升序排列的打卡日期。</param>
    /// <param name="today">用户本地的今天。</param>
    /// <remarks>
    /// 「当前连续」允许以<b>今天或昨天</b>结尾——今天还没吃饭不该算断签，
    /// 否则用户每天凌晨打开 App 都会看到「连续 0 天」，体验很挫败。
    /// </remarks>
    public static (int Current, int Longest) Compute(
        IReadOnlyList<DateOnly> sortedDistinctDates,
        DateOnly today)
    {
        if (sortedDistinctDates.Count == 0)
        {
            return (0, 0);
        }

        var longest = 1;
        var run = 1;

        for (var i = 1; i < sortedDistinctDates.Count; i++)
        {
            run = sortedDistinctDates[i].DayNumber - sortedDistinctDates[i - 1].DayNumber == 1
                ? run + 1
                : 1;

            longest = Math.Max(longest, run);
        }

        var last = sortedDistinctDates[^1];
        var gapToToday = today.DayNumber - last.DayNumber;

        // 最后一天既不是今天也不是昨天 → 已断签
        var current = gapToToday > 1 ? 0 : run;

        return (current, longest);
    }
}
