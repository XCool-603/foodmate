using FoodMate.Contracts.Profile;
using FoodMate.Contracts.Records;
using FoodMate.Core;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Profile;
using FoodMate.Infrastructure.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodMate.Infrastructure.Tests;

/// <summary>饮食记录服务的集成测试（真实 SQLite Provider）。</summary>
public class RecordServiceTests : SqliteTestBase
{
    private static readonly DateTimeOffset Now =
        new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);   // 北京时间 12:00

    private FixedClock Clock { get; } = new(Now);

    private RecordService CreateService(FoodMateDbContext db)
        => new(db, Clock, NullLogger<RecordService>.Instance);

    private RecordStatsService CreateStatsService(FoodMateDbContext db)
        => new(db, Clock);

    private static CreateRecordRequest CreateRequest(
        Guid? dishId = null,
        string? dishName = null,
        short mealType = (short)MealType.Lunch,
        double servings = 1.0,
        short? rating = null,
        bool? wouldEatAgain = null,
        short diningMode = (short)DiningMode.DineIn,
        DateTimeOffset? eatenAt = null,
        bool force = false)
        => new()
        {
            DishId = dishId,
            DishName = dishName,
            MealType = mealType,
            DiningMode = diningMode,
            Servings = servings,
            Rating = rating,
            WouldEatAgain = wouldEatAgain,
            EatenAt = eatenAt,
            Force = force,
        };

    // ── 创建与统计维护 ──────────────────────────────────────

    [Fact]
    public async Task 创建记录应同步维护统计表()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("番茄牛腩", cuisine: Cuisine.Sichuan, priceMin: 3200, priceMax: 4800);

        var service = CreateService(Db);
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, rating: 5, wouldEatAgain: true));

        var stat = await Db.UserDishStats.AsNoTracking()
            .FirstAsync(s => s.UserId == user.Id && s.DishId == dish.Id);

        Assert.Equal(1, stat.EatCount);
        Assert.Equal(5, stat.RatingSum);
        Assert.Equal(1, stat.RatingCount);
        Assert.Equal(1, stat.WouldEatAgainCount);
        Assert.Equal(0, stat.WouldNotEatAgainCount);
        Assert.Equal(Now, stat.LastEatenAt);

        var cuisineStat = await Db.UserCuisineStats.AsNoTracking()
            .FirstAsync(s => s.UserId == user.Id && s.Cuisine == Cuisine.Sichuan);

        Assert.Equal(1, cuisineStat.EatCountTotal);
    }

    [Fact]
    public async Task 份量系数应累加进食用次数()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("蛋炒饭");

        var service = CreateService(Db);
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, servings: 0.5));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, servings: 1.5));

        var stat = await Db.UserDishStats.AsNoTracking()
            .FirstAsync(s => s.UserId == user.Id && s.DishId == dish.Id);

        Assert.Equal(2, stat.EatCount);
    }

    [Fact]
    public async Task 双份记录不应被检查约束拒绝()
    {
        // 这条守的是：Servings 若用 decimal，SQLite 会存成 TEXT，
        // 于是 '2' <= '10' 的字符串比较会失败，正常记录反被拒。
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("红烧肉");

        var service = CreateService(Db);
        var record = await service.CreateAsync(user.Id, CreateRequest(dish.Id, servings: 2.0));

        Assert.Equal(2.0, record.Servings);
    }

    [Fact]
    public async Task 热量应按份量折算()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("番茄牛腩");
        dish.Calories = 400;
        await Db.SaveChangesAsync();

        var service = CreateService(Db);
        var record = await service.CreateAsync(user.Id, CreateRequest(dish.Id, servings: 1.5));

        Assert.Equal(600, record.Calories);
    }

    [Fact]
    public async Task 菜品快照应随记录一同保存()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync(
            "农家小炒肉",
            cuisine: Cuisine.Hunan,
            category: DishCategory.Meat,
            spicyLevel: 3,
            priceMin: 2600,
            priceMax: 3800);

        var service = CreateService(Db);
        var record = await service.CreateAsync(user.Id, CreateRequest(dish.Id));

        Assert.Equal(Cuisine.Hunan, record.DishSnapshot.Cuisine);
        Assert.Equal(3, record.DishSnapshot.SpicyLevel);
        Assert.Equal(3200, record.DishSnapshot.PriceCents);   // 区间中值

        // 即使菜品后来改名，历史记录仍保持当时的名字
        var tracked = await Db.Dishes.FirstAsync(d => d.Id == dish.Id);
        tracked.Name = "改名了";
        await Db.SaveChangesAsync();

        await using var fresh = CreateContext();
        var reloaded = await fresh.MealRecords.AsNoTracking().FirstAsync(r => r.Id == record.Id);
        Assert.Equal("农家小炒肉", reloaded.DishName);
    }

    [Fact]
    public async Task 手动输入未关联菜品也应能记录()
    {
        var user = await SeedUserAsync();

        var service = CreateService(Db);
        var record = await service.CreateAsync(user.Id, CreateRequest(dishName: "楼下那家牛肉面"));

        Assert.Null(record.DishId);
        Assert.Equal("楼下那家牛肉面", record.DishName);
        Assert.Empty(await Db.UserDishStats.AsNoTracking().Where(s => s.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task 既无菜品ID又无菜名应报参数错误()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(user.Id, CreateRequest()));

        Assert.Equal(ApiErrorCode.ValidationFailed, ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public async Task 非法份量应被拒绝(double servings)
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(user.Id, CreateRequest(dish.Id, servings: servings)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task 非法评分应被拒绝(short rating)
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(user.Id, CreateRequest(dish.Id, rating: rating)));
    }

    [Fact]
    public async Task 未来时间应被拒绝()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddHours(3))));
    }

    // ── 忌口冲突 ────────────────────────────────────────────

    [Fact]
    public async Task 忌口菜品应返回冲突码_确认后可强制写入()
    {
        var user = await SeedUserAsync();

        var preference = await Db.UserPreferences.FirstAsync(p => p.UserId == user.Id);
        preference.AvoidIngredients = ["花生"];
        await Db.SaveChangesAsync();

        var dish = await SeedDishAsync(
            "宫保鸡丁",
            ingredients: [new Ingredient("鸡丁", "300g"), new Ingredient("花生米", "50g", true)]);

        var service = CreateService(Db);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(user.Id, CreateRequest(dish.Id)));

        Assert.Equal(ApiErrorCode.AvoidIngredientConflict, ex.Code);
        Assert.Contains("花生米", ex.Message);

        // 用户确认后强制写入
        var record = await service.CreateAsync(user.Id, CreateRequest(dish.Id, force: true));
        Assert.Equal("宫保鸡丁", record.DishName);
    }

    // ── 更新与删除 ──────────────────────────────────────────

    [Fact]
    public async Task 补评分应更新统计()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        var record = await service.CreateAsync(user.Id, CreateRequest(dish.Id));

        await service.RateAsync(user.Id, record.Id, rating: 2, wouldEatAgain: false);

        var stat = await Db.UserDishStats.AsNoTracking()
            .FirstAsync(s => s.UserId == user.Id && s.DishId == dish.Id);

        Assert.Equal(2, stat.RatingSum);
        Assert.Equal(1, stat.RatingCount);
        Assert.Equal(0, stat.WouldEatAgainCount);
        Assert.Equal(1, stat.WouldNotEatAgainCount);
    }

    [Fact]
    public async Task 删除记录应回滚统计()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        var first = await service.CreateAsync(user.Id, CreateRequest(dish.Id, rating: 5));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, rating: 3));

        await service.DeleteAsync(user.Id, first.Id);

        var stat = await Db.UserDishStats.AsNoTracking()
            .FirstAsync(s => s.UserId == user.Id && s.DishId == dish.Id);

        Assert.Equal(1, stat.EatCount);
        Assert.Equal(3, stat.RatingSum);
        Assert.Equal(1, stat.RatingCount);
    }

    [Fact]
    public async Task 删完所有记录应移除统计行()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        var record = await service.CreateAsync(user.Id, CreateRequest(dish.Id));

        await service.DeleteAsync(user.Id, record.Id);

        Assert.Empty(await Db.UserDishStats.AsNoTracking()
            .Where(s => s.UserId == user.Id && s.DishId == dish.Id).ToListAsync());

        Assert.Empty(await Db.UserCuisineStats.AsNoTracking()
            .Where(s => s.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task 删除后最近食用时间应回退到上一条()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        var older = await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-3)));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-1)));

        await service.DeleteAsync(user.Id, older.Id);

        var stat = await Db.UserDishStats.AsNoTracking()
            .FirstAsync(s => s.UserId == user.Id && s.DishId == dish.Id);

        Assert.Equal(Now.AddDays(-1), stat.LastEatenAt);
    }

    // ── 查询 ────────────────────────────────────────────────

    [Fact]
    public async Task 列表应按就餐时间倒序()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-5)));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-1)));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-3)));

        var page = await service.ListAsync(user.Id, new RecordQuery());

        Assert.Equal(3, page.Total);
        Assert.Equal(Now.AddDays(-1), page.Items[0].EatenAt);
        Assert.Equal(Now.AddDays(-3), page.Items[1].EatenAt);
        Assert.Equal(Now.AddDays(-5), page.Items[2].EatenAt);
    }

    [Fact]
    public async Task 列表应支持未评分筛选()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, rating: 4));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id));

        var unrated = await service.ListAsync(user.Id, new RecordQuery { HasRating = false });

        Assert.Equal(1, unrated.Total);
    }

    [Fact]
    public async Task 待评分提醒应只返回窗口内的未评分记录()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-1)));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-3), rating: 5));
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-30)));

        var pending = await service.PendingRatingAsync(user.Id);

        Assert.Single(pending);
        Assert.Equal(Now.AddDays(-1), pending[0].EatenAt);
    }

    // ── 报表 ────────────────────────────────────────────────

    [Fact]
    public async Task 统计报表应汇总正确()
    {
        var user = await SeedUserAsync();
        var dishA = await SeedDishAsync("菜A", cuisine: Cuisine.Sichuan);
        var dishB = await SeedDishAsync("菜B", cuisine: Cuisine.Cantonese);

        var service = CreateService(Db);
        await service.CreateAsync(user.Id, CreateRequest(dishA.Id, rating: 5, eatenAt: Now.AddDays(-1)));
        await service.CreateAsync(user.Id, CreateRequest(dishB.Id, rating: 3, eatenAt: Now.AddDays(-2)));

        var stats = await CreateStatsService(Db).GetStatsAsync(user.Id, null, null);

        Assert.Equal(2, stats.TotalRecords);
        Assert.Equal(4.0, stats.AverageRating);
        Assert.Equal(2, stats.NewDishesTried);
        Assert.Equal(2, stats.RecordedDays);
        Assert.Equal(2, stats.CuisineDistribution.Count);
        Assert.Equal(2, stats.MealTypeDistribution.Sum(d => d.Count));
    }

    [Fact]
    public async Task 报表应区分首次尝试的菜品()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        // 上个月吃过一次
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-40)));
        // 本周再吃
        await service.CreateAsync(user.Id, CreateRequest(dish.Id, eatenAt: Now.AddDays(-1)));

        var stats = await CreateStatsService(Db).GetStatsAsync(user.Id, null, null);

        Assert.Equal(0, stats.NewDishesTried);
    }

    [Fact]
    public async Task 我的页卡片应给出连续天数与常吃菜系()
    {
        var user = await SeedUserAsync();
        var sichuan = await SeedDishAsync("川菜A", cuisine: Cuisine.Sichuan);
        var cantonese = await SeedDishAsync("粤菜B", cuisine: Cuisine.Cantonese);

        var service = CreateService(Db);
        await service.CreateAsync(user.Id, CreateRequest(sichuan.Id, eatenAt: Now));
        await service.CreateAsync(user.Id, CreateRequest(sichuan.Id, eatenAt: Now.AddDays(-1)));
        await service.CreateAsync(user.Id, CreateRequest(cantonese.Id, eatenAt: Now.AddDays(-2)));

        var summary = await CreateStatsService(Db).GetSummaryAsync(user.Id);

        Assert.Equal(3, summary.TotalRecords);
        Assert.Equal(2, summary.TotalDishesTried);
        Assert.Equal(3, summary.CurrentStreakDays);
        Assert.Equal(3, summary.LongestStreakDays);
        Assert.Equal(Cuisine.Sichuan, (Cuisine)summary.FavoriteCuisine!.Value);
    }
}

/// <summary>口味画像服务的集成测试。</summary>
public class PreferenceServiceTests : SqliteTestBase
{
    private static readonly DateTimeOffset Now =
        new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);

    private FixedClock Clock { get; } = new(Now);

    private RecordService Records(FoodMateDbContext db)
        => new(db, Clock, NullLogger<RecordService>.Instance);

    private PreferenceService CreateService(FoodMateDbContext db)
        => new(db, Records(db), Clock, NullLogger<PreferenceService>.Instance);

    [Fact]
    public async Task 从未设置过时应返回默认画像()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        var preference = await service.GetOrCreateAsync(user.Id);

        Assert.Equal(2, preference.SpicyLevel);
        Assert.Equal(1500, preference.BudgetMinCents);
        Assert.Equal(5000, preference.BudgetMaxCents);
        Assert.False(preference.OnboardingCompleted);
    }

    [Fact]
    public async Task 更新应只改传入的字段()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        await service.UpdateAsync(user.Id, new UpdatePreferenceRequest { SpicyLevel = 0 });

        var preference = await service.GetOrCreateAsync(user.Id);

        Assert.Equal(0, preference.SpicyLevel);
        Assert.Equal(1500, preference.BudgetMinCents);   // 未传，保持原值
    }

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)6)]
    public async Task 非法辣度应被拒绝(short spicy)
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(user.Id, new UpdatePreferenceRequest { SpicyLevel = spicy }));
    }

    [Fact]
    public async Task 预算上限低于下限应被拒绝()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(user.Id, new UpdatePreferenceRequest
            {
                BudgetMinCents = 8000,
                BudgetMaxCents = 3000,
            }));
    }

    [Fact]
    public async Task 忌口应去重并清理空白()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        var preference = await service.UpdateAsync(user.Id, new UpdatePreferenceRequest
        {
            AvoidIngredients = ["花生", " 花生 ", "", "   ", "香菜"],
        });

        Assert.Equal(2, preference.AvoidIngredients.Count);
        Assert.Contains("花生", preference.AvoidIngredients);
        Assert.Contains("香菜", preference.AvoidIngredients);
    }

    [Fact]
    public async Task 未知菜系取值应被拒绝()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(user.Id, new UpdatePreferenceRequest
            {
                PreferredCuisines = [99],
            }));
    }

    [Fact]
    public async Task 完成引导应设置标记与就餐方式权重()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        var preference = await service.CompleteOnboardingAsync(user.Id, new OnboardingRequest
        {
            SpicyLevel = 1,
            BudgetMinCents = 2000,
            BudgetMaxCents = 6000,
            PreferredCuisines = [(short)Cuisine.Cantonese],
            DiningMode = (short)DiningMode.Homemade,
        });

        Assert.True(preference.OnboardingCompleted);
        Assert.Equal(1, preference.SpicyLevel);
        Assert.Equal(0.6, preference.DiningModeWeights.Homemade, precision: 3);
        Assert.Equal(0.2, preference.DiningModeWeights.Takeout, precision: 3);
    }

    [Fact]
    public async Task 记录太少时不应给出画像建议()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        Assert.Null(await service.SuggestAsync(user.Id));
    }

    [Fact]
    public async Task 记录充足后应给出建议并可应用()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("川菜", cuisine: Cuisine.Sichuan, spicyLevel: 4);

        var records = Records(Db);

        for (var i = 0; i < 15; i++)
        {
            await records.CreateAsync(user.Id, new CreateRecordRequest
            {
                DishId = dish.Id,
                MealType = (short)MealType.Lunch,
                DiningMode = (short)DiningMode.Takeout,
                Rating = 5,
                EatenAt = Now.AddDays(-i % 10),
            });
        }

        var service = CreateService(Db);
        var suggestion = await service.SuggestAsync(user.Id);

        Assert.NotNull(suggestion);
        Assert.True(suggestion.HasAny);
        Assert.Equal(15, suggestion.SampleSize);
        Assert.Equal((short)4, suggestion.SpicyLevel);

        var applied = await service.ApplySuggestionAsync(user.Id);

        Assert.Equal(4, applied.SpicyLevel);
        Assert.Contains(Cuisine.Sichuan, applied.PreferredCuisines);
    }

    [Fact]
    public async Task 样本不足时应用建议应报错()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.ApplySuggestionAsync(user.Id));

        Assert.Equal(ApiErrorCode.ValidationFailed, ex.Code);
    }

    [Fact]
    public async Task 应用建议不应触碰忌口()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var service = CreateService(Db);
        await service.UpdateAsync(user.Id, new UpdatePreferenceRequest
        {
            AvoidIngredients = ["花生"],
        });

        var records = Records(Db);
        for (var i = 0; i < 15; i++)
        {
            await records.CreateAsync(user.Id, new CreateRecordRequest
            {
                DishId = dish.Id,
                MealType = (short)MealType.Lunch,
                DiningMode = (short)DiningMode.DineIn,
                EatenAt = Now.AddDays(-i % 10),
            });
        }

        await service.ApplySuggestionAsync(user.Id);

        var preference = await service.GetOrCreateAsync(user.Id);

        Assert.Equal(["花生"], preference.AvoidIngredients);
    }
}
