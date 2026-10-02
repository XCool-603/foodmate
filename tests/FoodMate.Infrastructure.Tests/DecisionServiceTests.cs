using FoodMate.Contracts.Decisions;
using FoodMate.Core;
using FoodMate.Core.Abstractions;
using FoodMate.Core.Decision;
using FoodMate.Core.Decision.Scoring;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Decisions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FoodMate.Infrastructure.Tests;

/// <summary>可控时钟，让决策结果不依赖当前时间。</summary>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>
/// 贯穿「数据库 → 应用服务 → 决策引擎」的集成测试。
/// </summary>
/// <remarks>
/// Core 的单元测试验证了引擎逻辑本身；这里验证的是<b>接线是否正确</b>：
/// 画像从库里读出来了吗？统计算对了吗？忌口真的在整条链路上生效了吗？
/// </remarks>
public class DecisionServiceTests : SqliteTestBase
{
    private static readonly DateTimeOffset Now =
        new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);   // 北京时间 12:00

    private DecisionService CreateService(FoodMateDbContext db, EngineOptions? options = null)
        => new(
            db,
            new DecisionEngine(
                scorers:
                [
                    new TasteScorer(),
                    new FreshnessScorer(),
                    new AffinityScorer(),
                    new TimeSlotScorer(),
                    new BudgetScorer(),
                    new ContextScorer(),
                    new ExplorationScorer(),
                ],
                penalties: new PenaltyEvaluator(),
                reasons: new ReasonGenerator(),
                random: DeterministicRandomSource.Instance),
            new FixedClock(Now),
            Options.Create(options ?? new EngineOptions()),
            NullLogger<DecisionService>.Instance);

    private static DecisionSuggestRequest LunchRequest(
        short diningMode = 0,
        short partySize = 1,
        string[]? moodTags = null,
        Guid[]? exclude = null,
        double jitter = 0)
        => new()
        {
            MealType = (short)MealType.Lunch,
            DiningMode = diningMode,
            PartySize = partySize,
            MoodTags = moodTags ?? [],
            ExcludeDishIds = exclude ?? [],
            ExploreJitter = jitter,
        };

    [Fact]
    public async Task 忌口应在完整链路上生效()
    {
        var user = await SeedUserAsync();

        // 给用户设置「花生」忌口
        var preference = await Db.UserPreferences.FirstAsync(p => p.UserId == user.Id);
        preference.AvoidIngredients = ["花生"];
        await Db.SaveChangesAsync();

        var peanutDish = await SeedDishAsync(
            "宫保鸡丁",
            ingredients: [new Ingredient("鸡丁", "300g"), new Ingredient("花生米", "50g", true)]);

        var safeDish = await SeedDishAsync(
            "清蒸鲈鱼",
            ingredients: [new Ingredient("鲈鱼", "1条")]);

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        Assert.DoesNotContain(outcome.Result.Ranked, r => r.DishId == peanutDish.Id);
        Assert.Contains(outcome.Result.Ranked, r => r.DishId == safeDish.Id);
    }

    [Fact]
    public async Task 忌口上位词应能匹配具体食材()
    {
        var user = await SeedUserAsync();

        var preference = await Db.UserPreferences.FirstAsync(p => p.UserId == user.Id);
        preference.AvoidIngredients = [CommonAllergens.TreeNut];   // 「坚果」
        await Db.SaveChangesAsync();

        // 菜品写的是具体坚果名
        await SeedDishAsync("腰果虾仁", ingredients: [new Ingredient("腰果", "80g", true)]);
        await SeedDishAsync("清炒时蔬", ingredients: [new Ingredient("青菜", "300g")]);

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        Assert.Single(outcome.Result.Ranked);
        Assert.Equal("清炒时蔬", outcome.Result.Ranked[0].Name);
    }

    [Fact]
    public async Task 刚吃过的菜应被二十四小时惩罚压下去()
    {
        var user = await SeedUserAsync();

        var justEaten = await SeedDishAsync("蛋炒饭", popularity: 95);
        var other = await SeedDishAsync("清蒸鲈鱼", popularity: 60);

        // 12 小时前刚吃过蛋炒饭
        await SeedRecordAsync(user.Id, justEaten.Id, "蛋炒饭", Now.AddHours(-12));

        // 同步统计表（真实流程由记录服务维护）
        Db.UserDishStats.Add(new UserDishStat
        {
            UserId = user.Id,
            DishId = justEaten.Id,
            EatCount = 1,
            LastEatenAt = Now.AddHours(-12),
            UpdatedAt = Now,
        });
        await Db.SaveChangesAsync();

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        var eaten = outcome.Result.Ranked.First(r => r.DishId == justEaten.Id);
        var fresh = outcome.Result.Ranked.First(r => r.DishId == other.Id);

        Assert.Contains(eaten.Penalties, p => p.Code == "EATEN_24H");
        Assert.True(fresh.Score > eaten.Score,
            "刚吃过 12 小时的菜应排在没吃过的新菜之后");
    }

    [Fact]
    public async Task 决策会话应落库并含打分明细()
    {
        var user = await SeedUserAsync();
        await SeedDishAsync("番茄牛腩", cuisine: Cuisine.Sichuan, spicyLevel: 1);

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        await using var fresh = CreateContext();
        var session = await fresh.DecisionSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == outcome.SessionId);

        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(MealType.Lunch, session.MealType);
        Assert.Equal("1.0.0", session.EngineVersion);
        Assert.NotEmpty(session.Candidates);

        var candidate = session.Candidates[0];
        Assert.Equal("番茄牛腩", candidate.Name);
        Assert.NotEmpty(candidate.Breakdown);
        Assert.NotEmpty(candidate.Reasons);
        Assert.Contains("taste", candidate.Breakdown.Keys);
    }

    [Fact]
    public async Task 回传选择应记录排名()
    {
        var user = await SeedUserAsync();
        await SeedDishAsync("菜A");
        await SeedDishAsync("菜B");
        await SeedDishAsync("菜C");

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        var target = outcome.Result.Ranked[2];   // 第 3 名

        var chosen = await service.ChooseAsync(
            user.Id, outcome.SessionId, target.DishId, "wheel");

        Assert.Equal(3, chosen.Rank);

        await using var fresh = CreateContext();
        var session = await fresh.DecisionSessions
            .AsNoTracking()
            .FirstAsync(s => s.Id == outcome.SessionId);

        Assert.Equal(target.DishId, session.ChosenDishId);
        Assert.NotNull(session.ChosenAt);
    }

    [Fact]
    public async Task 自己做模式应只返回有菜谱的菜()
    {
        var user = await SeedUserAsync();

        for (var i = 0; i < 10; i++)
        {
            await SeedDishAsync($"可自制{i}", withRecipe: true);
        }

        await SeedDishAsync("烤鸭", withRecipe: false);

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(
            user.Id, LunchRequest(diningMode: (short)DiningMode.Homemade));

        Assert.Equal(0, outcome.Result.RelaxedLevel);
        Assert.DoesNotContain(outcome.Result.Ranked, r => r.Name == "烤鸭");
    }

    [Fact]
    public async Task 冷启动应靠全局热度区分()
    {
        var user = await SeedUserAsync();   // 无任何记录

        await SeedDishAsync("冷门菜", popularity: 5);
        await SeedDishAsync("热门菜", popularity: 99);

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        Assert.Equal("热门菜", outcome.Result.Ranked[0].Name);
    }

    [Fact]
    public async Task 新鲜度应随记录变化_吃过的菜降权()
    {
        var user = await SeedUserAsync();

        var eaten = await SeedDishAsync("常吃的菜", popularity: 90);
        await SeedDishAsync("没吃过的菜", popularity: 90);

        // 昨天吃过，且近 30 天吃了 3 次
        Db.UserDishStats.Add(new UserDishStat
        {
            UserId = user.Id,
            DishId = eaten.Id,
            EatCount = 3,
            LastEatenAt = Now.AddDays(-1),
            UpdatedAt = Now,
        });

        for (var i = 1; i <= 3; i++)
        {
            await SeedRecordAsync(user.Id, eaten.Id, "常吃的菜", Now.AddDays(-i));
        }

        await Db.SaveChangesAsync();

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        var eatenScore = outcome.Result.Ranked.First(r => r.DishId == eaten.Id);
        var freshScore = outcome.Result.Ranked.First(r => r.Name == "没吃过的菜");

        Assert.True(freshScore.Score > eatenScore.Score,
            "同等热度下，没吃过的菜应排在最近反复吃的菜之前");
    }

    [Fact]
    public async Task 候选为空时不应抛异常()
    {
        var user = await SeedUserAsync();   // 一道菜都没有

        var service = CreateService(Db);
        var outcome = await service.SuggestAsync(user.Id, LunchRequest());

        Assert.Empty(outcome.Result.Ranked);
        Assert.Equal(0, outcome.Result.CandidateCount);
    }

    [Fact]
    public async Task 不存在的会话回传选择应抛业务异常()
    {
        var user = await SeedUserAsync();
        var service = CreateService(Db);

        var ex = await Assert.ThrowsAsync<Core.Exceptions.BusinessException>(
            () => service.ChooseAsync(user.Id, Guid.NewGuid(), Guid.NewGuid(), "list"));

        Assert.Equal(Core.Exceptions.ApiErrorCode.NotFound, ex.Code);
    }
}
