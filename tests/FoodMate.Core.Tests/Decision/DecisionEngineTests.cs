using FoodMate.Core.Decision;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Core.Exceptions;

namespace FoodMate.Core.Tests.Decision;

public class HardFilterTests
{
    [Fact]
    public void 含忌口食材的菜品必须被排除()
    {
        var peanutDish = Build.Dish(
            name: "宫保鸡丁",
            ingredients:
            [
                new Ingredient("鸡丁", "300g"),
                new Ingredient("花生米", "50g", IsCommonAllergen: true),
            ]);

        var safeDish = Build.Dish(
            name: "番茄炒蛋",
            ingredients: [new Ingredient("番茄", "2个"), new Ingredient("鸡蛋", "3个", IsCommonAllergen: true)]);

        var ctx = Build.Context(
            [peanutDish, safeDish],
            preference: new UserPreferenceSnapshot { AvoidIngredients = ["花生"] });

        var result = Build.Engine().Decide(ctx);

        Assert.DoesNotContain(result.Ranked, r => r.DishId == peanutDish.Id);
        Assert.Contains(result.Ranked, r => r.DishId == safeDish.Id);
        Assert.Equal(1, result.FilteredOutCount);
    }

    [Fact]
    public void 忌口上位词应展开为同义词()
    {
        // 用户写「坚果」，菜品写「腰果」——不展开同义词就匹配不上
        var cashewDish = Build.Dish(
            name: "腰果虾仁",
            ingredients: [new Ingredient("腰果", "80g", IsCommonAllergen: true)]);

        var ctx = Build.Context(
            [cashewDish],
            preference: new UserPreferenceSnapshot { AvoidIngredients = [CommonAllergens.TreeNut] });

        var result = Build.Engine().Decide(ctx);

        Assert.Empty(result.Ranked);
    }

    [Theory]
    [InlineData("花生", "花生米")]      // 用户词是菜品词的前缀
    [InlineData("花生米", "花生")]      // 菜品词是用户词的前缀
    [InlineData("香菜", "香菜末")]
    public void 忌口匹配应双向包含(string avoid, string ingredientName)
    {
        var dish = Build.Dish(ingredients: [new Ingredient(ingredientName)]);
        var expanded = HardFilter.ExpandAvoidList([avoid]);

        Assert.True(HardFilter.ContainsAvoided(dish, expanded));
    }

    [Fact]
    public void 单字食材名不应造成大面积误杀()
    {
        // 反向包含要求食材名 ≥ 2 字，避免「盐」这类单字把「盐焗鸡」以外的菜也误杀
        var dish = Build.Dish(ingredients: [new Ingredient("盐")]);
        var expanded = HardFilter.ExpandAvoidList(["椒盐"]);

        Assert.False(HardFilter.ContainsAvoided(dish, expanded));
    }

    [Fact]
    public void 自己做模式应排除不能在家做的菜但保留没录菜谱的菜()
    {
        // 有菜谱的菜足够多（≥ 转盘容量），因此不会触发降级
        var withRecipe = Enumerable.Range(0, 10)
            .Select(i => Build.Dish(name: $"可自制{i}", hasRecipe: true, cookMinutes: 15))
            .ToList();

        // 「没有缓存菜谱」不等于「做不了」—— 菜谱可以按需生成，所以不该被排除
        var cookableNoRecipe = Build.Dish(name: "蒜蓉西兰花", hasRecipe: false, canMakeAtHome: true);

        // 家里真做不了的才该被排除
        var notCookable = Build.Dish(name: "佛跳墙", hasRecipe: false, canMakeAtHome: false);

        var ctx = Build.Context(
            [.. withRecipe, cookableNoRecipe, notCookable],
            request: Build.Request(diningMode: DiningMode.Homemade));

        var result = Build.Engine().Decide(ctx);

        Assert.Equal(0, result.RelaxedLevel);
        Assert.Contains(result.Ranked, r => r.DishId == withRecipe[0].Id);

        // 没菜谱但在家能做 → 保留
        Assert.Contains(result.Ranked, r => r.DishId == cookableNoRecipe.Id);

        // 在家做不了 → 排除
        Assert.DoesNotContain(result.Ranked, r => r.DishId == notCookable.Id);
    }

    [Fact]
    public void 忌口过滤在任何降级层级都不放开()
    {
        // 只有 1 道菜，且含忌口 —— 降级也不能把它放进来
        var onlyDish = Build.Dish(ingredients: [new Ingredient("花生")]);

        var ctx = Build.Context(
            [onlyDish],
            preference: new UserPreferenceSnapshot { AvoidIngredients = ["花生"] },
            request: Build.Request(diningMode: DiningMode.Homemade));

        var result = Build.Engine().Decide(ctx);

        Assert.Empty(result.Ranked);
        Assert.Equal(0, result.CandidateCount);
    }
}

public class PenaltyEvaluatorTests
{
    private readonly PenaltyEvaluator _evaluator = new();

    [Fact]
    public void 十二小时前刚吃过应触发二十四小时惩罚()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(1, 1, Build.Now.AddHours(-12), 0, 0, 0),
            });

        var penalties = _evaluator.Evaluate(dish, ctx);

        var penalty = Assert.Single(penalties);
        Assert.Equal("EATEN_24H", penalty.Code);
        Assert.Equal(0.10, penalty.Multiplier, precision: 6);
    }

    [Fact]
    public void 两天前吃过不应触发二十四小时惩罚()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(1, 0, Build.Now.AddDays(-2), 0, 0, 0),
            });

        Assert.Empty(_evaluator.Evaluate(dish, ctx));
    }

    [Theory]
    [InlineData(1, 1, "DISLIKED", 0.25)]   // 均分 1.0
    [InlineData(4, 2, "DISLIKED", 0.25)]   // 均分 2.0（阈值）
    [InlineData(9, 3, "MEH", 0.70)]        // 均分 3.0（阈值）
    [InlineData(7, 2, "MEH", 0.70)]        // 均分 3.5 → 不触发
    public void 评分反馈应触发对应惩罚(int ratingSum, int ratingCount, string expectedCode, double multiplier)
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(1, 0, Build.Now.AddDays(-30), ratingSum, ratingCount, 0),
            });

        var penalties = _evaluator.Evaluate(dish, ctx);

        if (expectedCode == "MEH" && multiplier == 0.70 && ratingSum == 7)
        {
            Assert.Empty(penalties);   // 均分 3.5 高于阈值
            return;
        }

        var penalty = Assert.Single(penalties);
        Assert.Equal(expectedCode, penalty.Code);
        Assert.Equal(multiplier, penalty.Multiplier, precision: 6);
    }

    [Fact]
    public void 差评与一般般应互斥不叠加()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(1, 0, Build.Now.AddDays(-30), 2, 1, 0),   // 均分 2.0
            });

        var penalties = _evaluator.Evaluate(dish, ctx);

        Assert.Single(penalties);
        Assert.Equal("DISLIKED", penalties[0].Code);
    }

    [Fact]
    public void 明确不想再吃应触发惩罚()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                // 说过 2 次「不」、从未说过「是」
                [dish.Id] = new(3, 0, Build.Now.AddDays(-20), 0, 0, 0, WouldNotEatAgainCount: 2),
            });

        var penalties = _evaluator.Evaluate(dish, ctx);

        var penalty = Assert.Single(penalties);
        Assert.Equal("NEVER_AGAIN", penalty.Code);
    }

    [Fact]
    public void 说过不想吃但后来说过想再吃_不触发()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(5, 0, Build.Now.AddDays(-20), 0, 0, 2, WouldNotEatAgainCount: 1),
            });

        Assert.Empty(_evaluator.Evaluate(dish, ctx));
    }

    [Fact]
    public void 多个惩罚应相乘叠加()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                // 12 小时前吃过 + 打过 1 分
                [dish.Id] = new(1, 1, Build.Now.AddHours(-12), 1, 1, 0),
            });

        var penalties = _evaluator.Evaluate(dish, ctx);

        Assert.Equal(2, penalties.Count);

        var product = penalties.Aggregate(1.0, (acc, p) => acc * p.Multiplier);
        Assert.Equal(0.025, product, precision: 6);
    }
}

public class DecisionEngineTests
{
    /// <summary>
    /// 设计文档 §9 的完整算例：番茄牛腩。
    /// 手工推导的期望总分约 84.6。
    /// </summary>
    [Fact]
    public void 完整算例_番茄牛腩应得分约84点6()
    {
        var dish = Build.Dish(
            name: "番茄牛腩",
            cuisine: Cuisine.Sichuan,
            category: DishCategory.Meat,
            spicy: 1,
            priceMin: 3200,
            priceMax: 4800,
            calories: 420,
            ingredients: [new Ingredient("牛腩", "500g"), new Ingredient("番茄", "3个"), new Ingredient("洋葱", "半个")],
            tags: ["下饭", "暖胃", "炖菜"],
            mealTimes: MealTimeMask.Lunch | MealTimeMask.Dinner,
            seasons: SeasonMask.AllYear,
            hasRecipe: true,
            cookMinutes: 90,
            popularity: 88,
            distanceKm: 0.8);

        var ctx = Build.Context(
            [dish],
            preference: new UserPreferenceSnapshot
            {
                SpicyLevel = 2,
                BudgetMinCents = 2000,
                BudgetMaxCents = 5000,
                AvoidIngredients = [],
                PreferredCuisines = [Cuisine.Sichuan, Cuisine.Cantonese],
                MaxDistanceM = 2000,
            },
            request: Build.Request(
                mealType: MealType.Lunch,
                diningMode: DiningMode.DineIn,
                partySize: 1,
                weather: "rain",
                moodTags: [],
                location: new GeoPoint(31.2304, 121.4737)),
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                // 吃过 4 次，均分 4.5，3 次想再吃，最近一次 12 天前
                [dish.Id] = new(4, 0, Build.Now.AddDays(-12), 18, 4, 3),
            },
            cuisineStats: new Dictionary<Cuisine, CuisineStatSnapshot>
            {
                // 川菜近 30 天吃了 3 次，最近 2 天前
                [Cuisine.Sichuan] = new(8, 3, Build.Now.AddDays(-2)),
            });

        var result = Build.Engine().Decide(ctx);

        var scored = Assert.Single(result.Ranked);

        // 逐维度核对（对照设计文档 §9 的计算表）
        Assert.Equal(0.9725, scored.ScoreOf(ScoreDimension.Taste), precision: 3);
        Assert.Equal(0.7048, scored.ScoreOf(ScoreDimension.Freshness), precision: 3);
        Assert.Equal(0.7889, scored.ScoreOf(ScoreDimension.Affinity), precision: 3);
        Assert.Equal(1.0000, scored.ScoreOf(ScoreDimension.TimeSlot), precision: 3);
        Assert.Equal(1.0000, scored.ScoreOf(ScoreDimension.Budget), precision: 3);
        Assert.Equal(0.7833, scored.ScoreOf(ScoreDimension.Context), precision: 3);
        Assert.Equal(0.3000, scored.ScoreOf(ScoreDimension.Exploration), precision: 3);

        // 总分
        Assert.InRange(scored.Score, 84.0, 85.2);
        Assert.Empty(scored.Penalties);
        Assert.NotEmpty(scored.Reasons);
    }

    [Fact]
    public void 结果应按分数降序排列()
    {
        var good = Build.Dish(name: "很合适", spicy: 2, cuisine: Cuisine.Sichuan, popularity: 90);
        var meh = Build.Dish(name: "一般", spicy: 5, cuisine: Cuisine.Western, popularity: 20);

        var ctx = Build.Context(
            [good, meh],
            preference: new UserPreferenceSnapshot { SpicyLevel = 2, PreferredCuisines = [Cuisine.Sichuan] });

        var result = Build.Engine().Decide(ctx);

        Assert.Equal(2, result.Ranked.Count);
        Assert.Equal("很合适", result.Ranked[0].Name);
        Assert.True(result.Ranked[0].Score > result.Ranked[1].Score);
    }

    [Fact]
    public void 相同输入必须产生完全相同的结果()
    {
        var dishes = Enumerable.Range(0, 30)
            .Select(i => Build.Dish(name: $"菜{i}", spicy: (short)(i % 6), popularity: 50 + i))
            .ToList();

        var ctx = Build.Context(dishes, userRecordCount: 20);
        var engine = Build.Engine();

        var first = engine.Decide(ctx);
        var second = engine.Decide(ctx);

        Assert.Equal(
            first.Ranked.Select(r => (r.DishId, r.Score)),
            second.Ranked.Select(r => (r.DishId, r.Score)));
    }

    [Fact]
    public void 相同种子的抖动应可复现()
    {
        var dishes = Enumerable.Range(0, 20).Select(i => Build.Dish(name: $"菜{i}")).ToList();

        var ctx = Build.Context(dishes, request: Build.Request(exploreJitter: 3.0));

        var first = Build.Engine(new RandomSource(seed: 42)).Decide(ctx);
        var second = Build.Engine(new RandomSource(seed: 42)).Decide(ctx);

        Assert.Equal(
            first.Ranked.Select(r => (r.DishId, r.Score)),
            second.Ranked.Select(r => (r.DishId, r.Score)));
    }

    [Fact]
    public void 不同种子的抖动应产生不同结果()
    {
        var dishes = Enumerable.Range(0, 20).Select(i => Build.Dish(name: $"菜{i}")).ToList();

        var ctx = Build.Context(dishes, request: Build.Request(exploreJitter: 5.0));

        var a = Build.Engine(new RandomSource(seed: 1)).Decide(ctx);
        var b = Build.Engine(new RandomSource(seed: 999)).Decide(ctx);

        Assert.NotEqual(
            a.Ranked.Select(r => r.DishId).ToList(),
            b.Ranked.Select(r => r.DishId).ToList());
    }

    [Fact]
    public void 排除列表中的菜品不应出现()
    {
        var keep = Build.Dish(name: "保留");
        var drop = Build.Dish(name: "排除");

        var ctx = Build.Context(
            [keep, drop],
            request: Build.Request(excludeDishIds: [drop.Id]));

        var result = Build.Engine().Decide(ctx);

        Assert.Single(result.Ranked);
        Assert.Equal(keep.Id, result.Ranked[0].DishId);
    }

    [Fact]
    public void 候选为零时不应抛异常()
    {
        var ctx = Build.Context([]);

        var result = Build.Engine().Decide(ctx);

        Assert.Empty(result.Ranked);
        Assert.Equal(0, result.CandidateCount);
    }

    [Fact]
    public void 候选不足时应放宽就餐方式并标记降级()
    {
        // 「自己做」模式下只有 2 道菜有菜谱，但有 10 道菜
        var withRecipe = Enumerable.Range(0, 2)
            .Select(i => Build.Dish(name: $"有菜谱{i}", hasRecipe: true))
            .ToList();

        // 只有 2 道能在家做 → 硬过滤后不足转盘容量 → 触发降级
        var notCookable = Enumerable.Range(0, 10)
            .Select(i => Build.Dish(name: $"餐厅专属{i}", canMakeAtHome: false))
            .ToList();

        var ctx = Build.Context(
            [.. withRecipe, .. notCookable],
            request: Build.Request(diningMode: DiningMode.Homemade));

        var result = Build.Engine().Decide(ctx);

        Assert.Equal(1, result.RelaxedLevel);
        Assert.True(result.Ranked.Count >= 8, "降级后应能填满转盘");
    }

    [Fact]
    public void 候选充足时不应降级()
    {
        // 20 道菜全都有菜谱 → 「自己做」模式的硬过滤不会把候选削到 8 以下
        var dishes = Enumerable.Range(0, 20)
            .Select(i => Build.Dish(name: $"菜{i}", hasRecipe: true, cookMinutes: 20))
            .ToList();

        var ctx = Build.Context(dishes, request: Build.Request(diningMode: DiningMode.Homemade));

        var result = Build.Engine().Decide(ctx);

        Assert.Equal(0, result.RelaxedLevel);
    }

    [Fact]
    public void 每个结果都应至少有一条理由()
    {
        var dishes = Enumerable.Range(0, 15).Select(i => Build.Dish(name: $"菜{i}")).ToList();
        var ctx = Build.Context(dishes);

        var result = Build.Engine().Decide(ctx);

        Assert.NotEmpty(result.Ranked);
        Assert.All(result.Ranked, r => Assert.NotEmpty(r.Reasons));
    }

    [Fact]
    public void 理由不应超过两条()
    {
        var dishes = Enumerable.Range(0, 15).Select(i => Build.Dish(name: $"菜{i}")).ToList();
        var ctx = Build.Context(dishes);

        var result = Build.Engine().Decide(ctx);

        Assert.All(result.Ranked, r => Assert.InRange(r.Reasons.Count, 1, 2));
    }

    [Fact]
    public void 权重之和不为一时应快速失败()
    {
        var options = new EngineOptions();
        options.Weights.Taste = 0.50;   // 总和变成 1.25

        var ctx = Build.Context([Build.Dish()], options: options);

        var ex = Assert.Throws<InvalidOperationException>(() => Build.Engine().Decide(ctx));
        Assert.Contains("权重之和", ex.Message);
    }

    [Fact]
    public void 默认权重之和应为1()
    {
        var options = new EngineOptions();

        Assert.Equal(1.0, options.Weights.Sum, precision: 9);
        options.Validate();   // 不应抛异常
    }

    [Fact]
    public void 转盘候选应与榜单前N项同序()
    {
        var dishes = Enumerable.Range(0, 30).Select(i => Build.Dish(name: $"菜{i}")).ToList();
        var ctx = Build.Context(dishes);

        var result = Build.Engine().Decide(ctx);
        var wheel = result.WheelPicks(8);

        Assert.Equal(8, wheel.Count);
        Assert.Equal(result.Ranked.Take(8).Select(r => r.DishId), wheel.Select(r => r.DishId));
    }

    [Fact]
    public void 返回条数不应超过配置上限()
    {
        var dishes = Enumerable.Range(0, 100).Select(i => Build.Dish(name: $"菜{i}")).ToList();
        var ctx = Build.Context(dishes);

        var result = Build.Engine().Decide(ctx);

        Assert.Equal(20, result.Ranked.Count);   // TopN.ResultList 默认 20
    }

    [Fact]
    public void 两千候选的打分耗时应低于50毫秒()
    {
        var dishes = Enumerable.Range(0, 2000)
            .Select(i => Build.Dish(
                name: $"菜{i}",
                cuisine: (Cuisine)(i % 17),
                spicy: (short)(i % 6),
                popularity: i % 100,
                hasRecipe: i % 2 == 0))
            .ToList();

        var ctx = Build.Context(dishes, userRecordCount: 50);

        var result = Build.Engine().Decide(ctx);

        Assert.True(result.ElapsedMs < 50,
            $"2000 候选打分耗时 {result.ElapsedMs}ms，超出 50ms 预算");
    }
}

public class ReasonGeneratorTests
{
    [Fact]
    public void 应优先使用贡献最大的维度()
    {
        var dish = Build.Dish(
            name: "番茄牛腩",
            cuisine: Cuisine.Sichuan,
            spicy: 2,
            mealTimes: MealTimeMask.Lunch | MealTimeMask.Dinner);

        var ctx = Build.Context(
            [dish],
            preference: new UserPreferenceSnapshot { SpicyLevel = 2, PreferredCuisines = [Cuisine.Sichuan] },
            request: Build.Request(mealType: MealType.Lunch));

        var result = Build.Engine().Decide(ctx);
        var reasons = result.Ranked[0].Reasons;

        Assert.NotEmpty(reasons);
        Assert.All(reasons, r => Assert.False(string.IsNullOrWhiteSpace(r)));
    }

    [Fact]
    public void 好评过的高分菜应提到历史评分()
    {
        var dish = Build.Dish(name: "红烧肉");

        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(4, 0, Build.Now.AddDays(-10), 18, 4, 3),
            },
            userRecordCount: 20);

        var result = Build.Engine().Decide(ctx);
        var reasons = result.Ranked[0].Reasons;

        Assert.Contains(reasons, r => r.Contains("4.5") || r.Contains("吃过"));
    }

    [Fact]
    public void 理由中不应出现指责性表述()
    {
        var dishes = Enumerable.Range(0, 20)
            .Select(i => Build.Dish(name: $"菜{i}", spicy: (short)(i % 6)))
            .ToList();

        // 用户最近猛吃辣
        var ctx = Build.Context(
            dishes,
            preference: new UserPreferenceSnapshot { SpicyLevel = 5 },
            cuisineStats: new Dictionary<Cuisine, CuisineStatSnapshot>
            {
                [Cuisine.Other] = new(30, 20, Build.Now.AddDays(-1)),
            });

        var result = Build.Engine().Decide(ctx);

        var forbidden = new[] { "太多", "过量", "不健康", "你吃太" };
        foreach (var reason in result.Ranked.SelectMany(r => r.Reasons))
        {
            Assert.DoesNotContain(forbidden, word => reason.Contains(word));
        }
    }
}
