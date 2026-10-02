using FoodMate.Core.Decision;
using FoodMate.Core.Decision.Scoring;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;

namespace FoodMate.Core.Tests.Decision;

public class TasteScorerTests
{
    private readonly TasteScorer _scorer = new();

    /// <summary>设计文档 §5.1.1 的完整辣度对照表。</summary>
    [Theory]
    // pref = 0（完全不吃辣）：dish 3+ 触发安全护栏
    [InlineData(0, 0, 1.00)]
    [InlineData(0, 1, 0.85)]
    [InlineData(0, 2, 0.70)]
    [InlineData(0, 3, 0.10)]
    [InlineData(0, 4, 0.10)]
    [InlineData(0, 5, 0.10)]
    // pref = 1
    [InlineData(1, 0, 0.95)]
    [InlineData(1, 1, 1.00)]
    [InlineData(1, 2, 0.85)]
    [InlineData(1, 3, 0.70)]
    [InlineData(1, 4, 0.20)]
    [InlineData(1, 5, 0.20)]
    // pref = 2
    [InlineData(2, 0, 0.90)]
    [InlineData(2, 1, 0.95)]
    [InlineData(2, 2, 1.00)]
    [InlineData(2, 3, 0.85)]
    [InlineData(2, 4, 0.70)]
    [InlineData(2, 5, 0.55)]
    // pref = 3
    [InlineData(3, 0, 0.85)]
    [InlineData(3, 5, 0.70)]
    // pref = 4
    [InlineData(4, 0, 0.80)]
    [InlineData(4, 5, 0.85)]
    // pref = 5（吃辣）：dish 0 只是「有点淡」
    [InlineData(5, 0, 0.75)]
    [InlineData(5, 5, 1.00)]
    public void 辣度打分应符合对照表(short pref, short dish, double expected)
    {
        var actual = TasteScorer.ScoreSpicy(dish, pref);

        Assert.Equal(expected, actual, precision: 6);
    }

    [Fact]
    public void 不吃辣遇重辣应被安全护栏拦截()
    {
        // 这道菜不该在转盘上被抽到
        Assert.Equal(0.10, TasteScorer.ScoreSpicy(dishSpicy: 5, prefSpicy: 0));
    }

    [Fact]
    public void 辣度惩罚应对称性偏低_更辣罚得比更淡重()
    {
        // 偏好 2：吃到 4（更辣）vs 吃到 0（更淡）
        var tooSpicy = TasteScorer.ScoreSpicy(dishSpicy: 4, prefSpicy: 2);
        var tooMild = TasteScorer.ScoreSpicy(dishSpicy: 0, prefSpicy: 2);

        Assert.True(tooSpicy < tooMild, "比偏好更辣应罚得比更淡重");
    }

    [Fact]
    public void 菜系偏好为空时应中性_不惩罚任何菜系()
    {
        Assert.Equal(0.5, TasteScorer.ScoreCuisine(Cuisine.Sichuan, []));
        Assert.Equal(0.5, TasteScorer.ScoreCuisine(Cuisine.Western, []));
    }

    [Fact]
    public void 命中偏好菜系应满分_未命中应降分()
    {
        var preferred = new List<Cuisine> { Cuisine.Sichuan, Cuisine.Cantonese };

        Assert.Equal(1.0, TasteScorer.ScoreCuisine(Cuisine.Sichuan, preferred));
        Assert.Equal(0.4, TasteScorer.ScoreCuisine(Cuisine.Hunan, preferred));
    }

    [Fact]
    public void 无快捷需求时_口味分只由辣度与菜系构成()
    {
        var dish = Build.Dish(cuisine: Cuisine.Sichuan, spicy: 2);
        var ctx = Build.Context(
            [dish],
            preference: new UserPreferenceSnapshot
            {
                SpicyLevel = 2,
                PreferredCuisines = [Cuisine.Sichuan],
            },
            request: Build.Request(moodTags: []));

        // 0.55×1.0 + 0.45×1.0
        Assert.Equal(1.0, _scorer.Score(dish, ctx), precision: 6);
    }

    [Fact]
    public void 快捷需求应参与打分()
    {
        var spicyDish = Build.Dish(name: "麻辣香锅", spicy: 4, tags: ["重口"]);
        var lightDish = Build.Dish(name: "清蒸鱼", spicy: 0, tags: ["清淡"]);

        var ctx = Build.Context(
            [spicyDish, lightDish],
            request: Build.Request(moodTags: [MoodTags.Light]));

        var spicyScore = _scorer.Score(spicyDish, ctx);
        var lightScore = _scorer.Score(lightDish, ctx);

        Assert.True(lightScore > spicyScore, "勾选「清淡的」时清淡菜应得分更高");
    }

    [Fact]
    public void 未知快捷标签应从中剔除_不惩罚()
    {
        var dish = Build.Dish();
        var ctx = Build.Context([dish], request: Build.Request(moodTags: ["不存在的标签"]));

        // 未知标签全部剔除 → S_mood 中性 0.5，不因未知标签扣分
        var score = _scorer.Score(dish, ctx);

        Assert.True(score > 0.4, "未知标签不应把分数拉到很低");
    }
}

public class FreshnessScorerTests
{
    private readonly FreshnessScorer _scorer = new();
    private static readonly FreshnessOptions Options = new();

    [Fact]
    public void 从未吃过应为满分()
    {
        var dish = Build.Dish();
        var ctx = Build.Context([dish]);

        Assert.Equal(1.0, _scorer.Score(dish, ctx), precision: 6);
    }

    [Fact]
    public void 今天刚吃过菜品级应归零()
    {
        var actual = FreshnessScorer.Compute(
            lastEatenAt: Build.Now,
            eatCount30d: 1,
            now: Build.Now,
            tauDays: Options.DishTauDays,
            options: Options);

        Assert.Equal(0.0, actual, precision: 6);
    }

    [Fact]
    public void 昨天吃过应被显著抑制()
    {
        var actual = FreshnessScorer.Compute(
            lastEatenAt: Build.Now.AddDays(-1),
            eatCount30d: 1,
            now: Build.Now,
            tauDays: Options.DishTauDays,
            options: Options);

        Assert.InRange(actual, 0.20, 0.35);
    }

    [Fact]
    public void 很久没吃应基本恢复()
    {
        var actual = FreshnessScorer.Compute(
            lastEatenAt: Build.Now.AddDays(-30),
            eatCount30d: 0,
            now: Build.Now,
            tauDays: Options.DishTauDays,
            options: Options);

        Assert.InRange(actual, 0.95, 1.0);
    }

    [Fact]
    public void 吃过次数越多抑制越强()
    {
        var once = FreshnessScorer.Compute(
            Build.Now.AddDays(-2), eatCount30d: 1, Build.Now, Options.DishTauDays, Options);

        var thrice = FreshnessScorer.Compute(
            Build.Now.AddDays(-2), eatCount30d: 3, Build.Now, Options.DishTauDays, Options);

        Assert.True(thrice < once, "近 30 天吃得越多，新鲜度应越低");
    }

    [Fact]
    public void 菜系级新鲜度应参与合成()
    {
        var dish = Build.Dish(cuisine: Cuisine.Sichuan);
        var id = dish.Id;

        // 同一道菜从没吃过，但菜系前天刚吃过
        var ctx = Build.Context(
            [dish],
            cuisineStats: new Dictionary<Cuisine, CuisineStatSnapshot>
            {
                [Cuisine.Sichuan] = new(EatCountTotal: 5, EatCount30d: 3, LastEatenAt: Build.Now.AddDays(-2)),
            });

        var withCuisineHistory = _scorer.Score(dish, ctx);
        var withoutHistory = _scorer.Score(dish, Build.Context([dish]));

        Assert.True(withCuisineHistory < withoutHistory,
            "菜系最近吃得多时，即使这道菜没吃过也应被适度抑制");
        Assert.True(withCuisineHistory > 0.6, "菜系级权重只占 0.3，不应过度抑制");
    }
}

public class AffinityScorerTests
{
    private readonly AffinityScorer _scorer = new();

    [Fact]
    public void 从未吃过应返回中性偏低分()
    {
        var dish = Build.Dish();
        var ctx = Build.Context([dish]);

        // 0.5×0.5 + 0.3×0.3 + 0.2×0.5 = 0.44
        Assert.Equal(0.44, _scorer.Score(dish, ctx), precision: 6);
    }

    [Fact]
    public void 高分好评应拉高偏好强度()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(EatCount: 5, EatCount30d: 1, LastEatenAt: Build.Now.AddDays(-10),
                                RatingSum: 22, RatingCount: 5, WouldEatAgainCount: 3),
            });

        var score = _scorer.Score(dish, ctx);

        Assert.InRange(score, 0.74, 0.82);
    }

    [Fact]
    public void 差评应拉低偏好强度()
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(EatCount: 1, EatCount30d: 0, LastEatenAt: Build.Now.AddDays(-40),
                                RatingSum: 2, RatingCount: 1, WouldEatAgainCount: 0),
            });

        Assert.True(_scorer.Score(dish, ctx) < 0.25);
    }

    [Fact]
    public void 冷启动时应用全局热度兜底()
    {
        var popular = Build.Dish(popularity: 95);
        var obscure = Build.Dish(popularity: 10);

        var ctx = Build.Context([popular, obscure], userRecordCount: 0);

        Assert.True(_scorer.Score(popular, ctx) > _scorer.Score(obscure, ctx),
            "新用户第一次用时应靠全局热度区分");
    }

    [Fact]
    public void 有足够记录后不再使用热度兜底()
    {
        var popular = Build.Dish(popularity: 95);
        var obscure = Build.Dish(popularity: 10);

        var ctx = Build.Context([popular, obscure], userRecordCount: 50);

        // 都没有个人历史 → 两者应完全相同，热度不参与
        Assert.Equal(_scorer.Score(popular, ctx), _scorer.Score(obscure, ctx), precision: 6);
    }
}

public class TimeSlotScorerTests
{
    private readonly TimeSlotScorer _scorer = new();

    [Fact]
    public void 午餐时段午餐菜应满分()
    {
        var dish = Build.Dish(mealTimes: MealTimeMask.Lunch | MealTimeMask.Dinner);
        var ctx = Build.Context([dish], request: Build.Request(mealType: MealType.Lunch));

        Assert.Equal(1.0, _scorer.Score(dish, ctx), precision: 6);
    }

    [Fact]
    public void 早餐推荐午晚餐菜应被显著抑制()
    {
        var braisedPork = Build.Dish(name: "红烧肉", mealTimes: MealTimeMask.Lunch | MealTimeMask.Dinner);
        var ctx = Build.Context([braisedPork], request: Build.Request(mealType: MealType.Breakfast));

        // 0.8×0.10 + 0.2×1.00
        Assert.Equal(0.28, _scorer.Score(braisedPork, ctx), precision: 6);
    }

    [Fact]
    public void 反季菜应被轻微抑制()
    {
        var summerDish = Build.Dish(name: "凉拌黄瓜", seasons: SeasonMask.Summer);
        var ctx = Build.Context([summerDish], request: Build.Request(mealType: MealType.Lunch));

        // 6 月是夏季 → 应命中
        Assert.Equal(1.0, _scorer.Score(summerDish, ctx), precision: 6);

        // 12 月是冬季 → 应降分
        var winterCtx = Build.Context([summerDish], request: new DecisionRequest
        {
            MealType = MealType.Lunch,
            Now = new DateTimeOffset(2025, 12, 15, 4, 0, 0, TimeSpan.Zero),
            LocalOffset = TimeSpan.FromHours(8),
        });

        // 0.8×1.00 + 0.2×0.65
        Assert.Equal(0.93, _scorer.Score(summerDish, winterCtx), precision: 6);
    }

    [Fact]
    public void 四季皆宜的菜不应受季节影响()
    {
        var dish = Build.Dish(seasons: SeasonMask.AllYear);
        var ctx = Build.Context([dish], request: Build.Request(mealType: MealType.Lunch));

        Assert.Equal(1.0, _scorer.Score(dish, ctx), precision: 6);
    }
}

public class BudgetScorerTests
{
    [Theory]
    [InlineData(4000, 2000, 5000, 1.00)]   // 区间内
    [InlineData(2000, 2000, 5000, 1.00)]   // 下限
    [InlineData(5000, 2000, 5000, 1.00)]   // 上限
    [InlineData(1500, 2000, 5000, 0.925)]  // 比预算便宜，轻微降分
    [InlineData(6000, 2000, 5000, 0.60)]   // 超 20%
    [InlineData(7500, 2000, 5000, 0.00)]   // 超 50% → 归零
    [InlineData(10000, 2000, 5000, 0.00)]  // 超 100% → 归零
    public void 预算打分应符合规则(int price, int min, int max, double expected)
    {
        Assert.Equal(expected, BudgetScorer.Score(price, min, max), precision: 6);
    }

    [Fact]
    public void 未设预算时应中性()
    {
        Assert.Equal(0.5, BudgetScorer.Score(9999, 0, 0));
    }

    [Fact]
    public void 价格缺失时应按分类默认价兜底()
    {
        var soup = Build.Dish(category: DishCategory.Soup, priceMin: null, priceMax: null);

        Assert.Equal(CategoryDefaultPrice.For(DishCategory.Soup), soup.ReferencePriceCents);

        var hotpot = Build.Dish(category: DishCategory.Hotpot, priceMin: null, priceMax: null);
        Assert.Equal(CategoryDefaultPrice.For(DishCategory.Hotpot), hotpot.ReferencePriceCents);
    }
}

public class ContextScorerTests
{
    private readonly ContextScorer _scorer = new();

    [Fact]
    public void 自己做模式必须要求菜谱()
    {
        var withRecipe = Build.Dish(hasRecipe: true);
        var withoutRecipe = Build.Dish(hasRecipe: false);

        Assert.True(ContextScorer.SupportsDiningMode(withRecipe, DiningMode.Homemade));
        Assert.False(ContextScorer.SupportsDiningMode(withoutRecipe, DiningMode.Homemade));
    }

    [Fact]
    public void 缺失维度应重新归一化_而非打低分()
    {
        var dish = Build.Dish();
        var ctx = Build.Context([dish], request: Build.Request(diningMode: DiningMode.DineIn));

        // 无定位、无天气 → 只有「就餐方式」与「人数」参与
        var score = _scorer.Score(dish, ctx);

        Assert.InRange(score, 0.7, 1.0);
    }

    [Theory]
    [InlineData("rain", 0.80)]   // 暖胃菜：0.6 + 0.2
    [InlineData("cold", 0.90)]   // 暖胃菜：0.6 + 0.3
    [InlineData("snow", 0.90)]
    [InlineData("clear", 0.60)]  // 中性
    public void 天气打分应偏好暖胃菜(string weather, double expected)
    {
        var warming = Build.Dish(category: DishCategory.Soup, tags: ["暖胃"]);

        Assert.Equal(expected, ContextScorer.WeatherScore(warming, weather), precision: 6);
    }

    [Fact]
    public void 高温天应抑制重辣与火锅()
    {
        var spicyHotpot = Build.Dish(category: DishCategory.Hotpot, spicy: 5);

        // 0.6 - 0.3
        Assert.Equal(0.30, ContextScorer.WeatherScore(spicyHotpot, "hot"), precision: 6);
    }

    [Fact]
    public void 一个人吃应抑制火锅()
    {
        var hotpot = Build.Dish(category: DishCategory.Hotpot);

        Assert.Equal(0.20, ContextScorer.PartySizeScore(hotpot, partySize: 1), precision: 6);
    }

    [Fact]
    public void 多人聚餐应偏好火锅()
    {
        var hotpot = Build.Dish(category: DishCategory.Hotpot);

        Assert.Equal(1.00, ContextScorer.PartySizeScore(hotpot, partySize: 5), precision: 6);
    }

    [Fact]
    public void 距离应在可接受范围内线性衰减()
    {
        var near = Build.Dish(distanceKm: 0.2);
        var far = Build.Dish(distanceKm: 1.8);

        var ctx = Build.Context(
            [near, far],
            preference: new UserPreferenceSnapshot { MaxDistanceM = 2000 },
            request: Build.Request(
                diningMode: DiningMode.DineIn,
                location: new GeoPoint(31.23, 121.47)));

        Assert.True(_scorer.Score(near, ctx) > _scorer.Score(far, ctx), "更近的店应得分更高");
    }
}

public class ExplorationScorerTests
{
    private readonly ExplorationScorer _scorer = new();

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 0.7)]
    [InlineData(2, 0.7)]
    [InlineData(3, 0.3)]
    [InlineData(10, 0.3)]
    public void 探索度应随食用次数递减(int eatCount, double expected)
    {
        var dish = Build.Dish();
        var ctx = Build.Context(
            [dish],
            dishStats: new Dictionary<Guid, DishStatSnapshot>
            {
                [dish.Id] = new(eatCount, 0, Build.Now.AddDays(-5), 0, 0, 0),
            });

        Assert.Equal(expected, _scorer.Score(dish, ctx), precision: 6);
    }
}
