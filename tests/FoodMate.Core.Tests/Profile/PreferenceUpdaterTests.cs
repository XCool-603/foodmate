using FoodMate.Core.Enums;
using FoodMate.Core.Profile;

namespace FoodMate.Core.Tests.Profile;

public class PreferenceUpdaterTests
{
    private static readonly DateTimeOffset Now =
        new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);

    private static LearnedRecordFact Fact(
        Cuisine cuisine = Cuisine.Other,
        short spicy = 2,
        int? price = 3000,
        DiningMode mode = DiningMode.DineIn,
        short? rating = null,
        int daysAgo = 0)
        => new(cuisine, spicy, price, mode, rating, Now.AddDays(-daysAgo));

    private static List<LearnedRecordFact> Repeat(int count, Func<int, LearnedRecordFact> factory)
        => [.. Enumerable.Range(0, count).Select(factory)];

    [Fact]
    public void 样本不足时应返回null()
    {
        var history = Repeat(5, _ => Fact());

        Assert.Null(PreferenceUpdater.Suggest(history, Now));
    }

    [Fact]
    public void 样本充足时应给出建议()
    {
        var history = Repeat(20, i => Fact(spicy: 2, price: 3000, daysAgo: i % 10));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.Equal(20, suggestion.SampleSize);
        Assert.True(suggestion.HasAny);
    }

    [Fact]
    public void 辣度应取加权中位数_而非均值()
    {
        // 19 次「不辣」+ 1 次「变态辣」
        var history = new List<LearnedRecordFact>();
        history.AddRange(Repeat(19, i => Fact(spicy: 0, daysAgo: i % 20)));
        history.Add(Fact(spicy: 5));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        // 均值会是 0.25，中位数应为 0——一次猎奇不该把偏好拉高
        Assert.Equal((short)0, suggestion.SpicyLevel);
    }

    [Fact]
    public void 高分记录的辣度权重更大()
    {
        // 一半 0 辣但都差评，一半 4 辣但都好评
        var history = new List<LearnedRecordFact>();
        history.AddRange(Repeat(10, i => Fact(spicy: 0, rating: 1, daysAgo: i % 10)));
        history.AddRange(Repeat(10, i => Fact(spicy: 4, rating: 5, daysAgo: i % 10)));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        // 爱吃的更能代表真实口味
        Assert.True(suggestion.SpicyLevel >= 3,
            $"期望偏辣（≥3），实际 {suggestion.SpicyLevel}");
    }

    [Fact]
    public void 辣度只统计时间窗口内的记录()
    {
        var history = new List<LearnedRecordFact>();
        // 窗口内（30 天）全是 1 辣
        history.AddRange(Repeat(10, i => Fact(spicy: 1, daysAgo: i % 25)));
        // 窗口外（60 天前）全是 5 辣
        history.AddRange(Repeat(10, _ => Fact(spicy: 5, daysAgo: 60)));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.Equal((short)1, suggestion.SpicyLevel);
    }

    [Fact]
    public void 预算应取分位数_而非最值()
    {
        var history = new List<LearnedRecordFact>();
        // 绝大多数 ¥30
        history.AddRange(Repeat(18, i => Fact(price: 3000, daysAgo: i % 15)));
        // 一次奢侈 ¥500，一次凑合 ¥5
        history.Add(Fact(price: 50000, daysAgo: 1));
        history.Add(Fact(price: 500, daysAgo: 2));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.NotNull(suggestion.BudgetMinCents);
        Assert.NotNull(suggestion.BudgetMaxCents);

        // 极值不应定义常规预算
        Assert.True(suggestion.BudgetMinCents >= 1000,
            $"下限 {suggestion.BudgetMinCents} 被单次凑合拉得过低");
        Assert.True(suggestion.BudgetMaxCents <= 10000,
            $"上限 {suggestion.BudgetMaxCents} 被单次奢侈拉得过高");
    }

    [Fact]
    public void 预算应取整到元()
    {
        var history = Repeat(10, i => Fact(price: 3333, daysAgo: i));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.Equal(0, suggestion.BudgetMinCents!.Value % 100);
        Assert.Equal(0, suggestion.BudgetMaxCents!.Value % 100);
    }

    [Fact]
    public void 偏好菜系应取频次最高的前几名()
    {
        var history = new List<LearnedRecordFact>();
        history.AddRange(Repeat(8, i => Fact(cuisine: Cuisine.Sichuan, daysAgo: i)));
        history.AddRange(Repeat(5, i => Fact(cuisine: Cuisine.Cantonese, daysAgo: i)));
        history.AddRange(Repeat(3, i => Fact(cuisine: Cuisine.Hunan, daysAgo: i)));
        history.AddRange(Repeat(2, i => Fact(cuisine: Cuisine.Western, daysAgo: i)));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.NotNull(suggestion.PreferredCuisines);
        Assert.Equal(3, suggestion.PreferredCuisines.Count);
        Assert.Equal(Cuisine.Sichuan, suggestion.PreferredCuisines[0]);
        Assert.Contains(Cuisine.Cantonese, suggestion.PreferredCuisines);
        Assert.DoesNotContain(Cuisine.Western, suggestion.PreferredCuisines);
    }

    [Fact]
    public void 只出现一次的菜系不应纳入偏好()
    {
        var history = new List<LearnedRecordFact>();
        history.AddRange(Repeat(10, i => Fact(cuisine: Cuisine.Sichuan, daysAgo: i)));
        history.Add(Fact(cuisine: Cuisine.Japanese));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.DoesNotContain(Cuisine.Japanese, suggestion.PreferredCuisines ?? []);
    }

    [Fact]
    public void 家常菜不应作为偏好菜系()
    {
        var history = Repeat(15, i => Fact(cuisine: Cuisine.Other, daysAgo: i % 10));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.Null(suggestion.PreferredCuisines);
    }

    [Fact]
    public void 就餐方式权重应反映历史占比()
    {
        var history = new List<LearnedRecordFact>();
        history.AddRange(Repeat(6, i => Fact(mode: DiningMode.Takeout, daysAgo: i)));
        history.AddRange(Repeat(3, i => Fact(mode: DiningMode.DineIn, daysAgo: i)));
        history.AddRange(Repeat(1, i => Fact(mode: DiningMode.Homemade, daysAgo: i)));

        var suggestion = PreferenceUpdater.Suggest(history, Now);

        Assert.NotNull(suggestion);
        Assert.NotNull(suggestion.DiningModeWeights);

        Assert.Equal(0.6, suggestion.DiningModeWeights.Takeout, precision: 3);
        Assert.Equal(0.3, suggestion.DiningModeWeights.DineIn, precision: 3);
        Assert.Equal(0.1, suggestion.DiningModeWeights.Homemade, precision: 3);
    }

    [Fact]
    public void 画像建议不应包含忌口字段()
    {
        // 忌口是安全字段，绝不由算法推断——PreferenceSuggestion 里根本没有这个属性。
        // 这个测试用反射守住这条约束，防止将来有人「顺手」加上。
        var properties = typeof(PreferenceSuggestion)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(properties, name =>
            name.Contains("Avoid", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Allergen", StringComparison.OrdinalIgnoreCase));
    }
}

public class StreakTests
{
    private static readonly DateOnly Today = new(2025, 6, 15);

    [Fact]
    public void 无记录时连续天数为零()
    {
        var (current, longest) = StreakCalculator.Compute([], Today);

        Assert.Equal(0, current);
        Assert.Equal(0, longest);
    }

    [Fact]
    public void 连续到今天应正确计数()
    {
        var dates = new List<DateOnly>
        {
            Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1), Today,
        };

        var (current, longest) = StreakCalculator.Compute(dates, Today);

        Assert.Equal(4, current);
        Assert.Equal(4, longest);
    }

    [Fact]
    public void 今天还没吃不应算断签()
    {
        var dates = new List<DateOnly> { Today.AddDays(-2), Today.AddDays(-1) };

        var (current, _) = StreakCalculator.Compute(dates, Today);

        Assert.Equal(2, current);
    }

    [Fact]
    public void 中断两天应算断签()
    {
        var dates = new List<DateOnly> { Today.AddDays(-4), Today.AddDays(-3) };

        var (current, longest) = StreakCalculator.Compute(dates, Today);

        Assert.Equal(0, current);
        Assert.Equal(2, longest);
    }

    [Fact]
    public void 最长连续应取历史最大值()
    {
        var dates = new List<DateOnly>
        {
            Today.AddDays(-20), Today.AddDays(-19), Today.AddDays(-18), Today.AddDays(-17),
            Today.AddDays(-16),
            Today.AddDays(-2), Today.AddDays(-1),
        };

        var (current, longest) = StreakCalculator.Compute(dates, Today);

        Assert.Equal(2, current);
        Assert.Equal(5, longest);
    }
}
