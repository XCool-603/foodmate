using FoodMate.Core;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;

namespace FoodMate.Core.Tests;

public class MealTimesTests
{
    [Theory]
    [InlineData(5, 0, MealType.Breakfast)]   // 边界：早餐起点
    [InlineData(6, 30, MealType.Breakfast)]
    [InlineData(9, 59, MealType.Breakfast)]  // 边界：早餐终点
    [InlineData(10, 0, MealType.Lunch)]      // 边界：午餐起点
    [InlineData(12, 0, MealType.Lunch)]
    [InlineData(14, 59, MealType.Lunch)]     // 边界：午餐终点
    [InlineData(15, 0, MealType.Dinner)]     // 边界：晚餐起点
    [InlineData(18, 30, MealType.Dinner)]
    [InlineData(20, 59, MealType.Dinner)]    // 边界：晚餐终点
    [InlineData(21, 0, MealType.LateNight)]  // 边界：夜宵起点
    [InlineData(23, 59, MealType.LateNight)]
    [InlineData(0, 0, MealType.LateNight)]   // 跨午夜
    [InlineData(2, 0, MealType.LateNight)]
    [InlineData(4, 59, MealType.LateNight)]  // 边界：夜宵终点
    public void Infer_应按本地时间推断餐次(int hour, int minute, MealType expected)
    {
        var actual = MealTimes.Infer(new TimeOnly(hour, minute));

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(MealType.Breakfast, MealTimeMask.Breakfast)]
    [InlineData(MealType.Lunch, MealTimeMask.Lunch)]
    [InlineData(MealType.Dinner, MealTimeMask.Dinner)]
    [InlineData(MealType.LateNight, MealTimeMask.LateNight)]
    public void ToMask_应返回对应位掩码(MealType mealType, MealTimeMask expected)
    {
        Assert.Equal(expected, MealTimes.ToMask(mealType));
    }

    [Fact]
    public void All_应覆盖全部四个餐次()
    {
        Assert.Equal(15, (int)MealTimeMask.All);

        foreach (var mealType in Enum.GetValues<MealType>())
        {
            Assert.True(MealTimeMask.All.HasFlag(MealTimes.ToMask(mealType)),
                $"{mealType} 未包含在 MealTimeMask.All 中");
        }
    }

    [Theory]
    [InlineData(MealType.Breakfast, "早餐")]
    [InlineData(MealType.Lunch, "午餐")]
    [InlineData(MealType.Dinner, "晚餐")]
    [InlineData(MealType.LateNight, "夜宵")]
    public void Label_应返回中文名称(MealType mealType, string expected)
    {
        Assert.Equal(expected, MealTimes.Label(mealType));
    }

    [Fact]
    public void InferFromUtc_应按给定时区偏移换算()
    {
        // UTC 04:00 = 北京时间 12:00（+8）→ 午餐
        var utc = new DateTimeOffset(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(MealType.Lunch, MealTimes.InferFromUtc(utc, TimeSpan.FromHours(8)));

        // 同一时刻在 UTC 时区是 04:00 → 夜宵
        Assert.Equal(MealType.LateNight, MealTimes.InferFromUtc(utc, TimeSpan.Zero));
    }
}

public class EnumLabelsTests
{
    [Theory]
    [InlineData(0, "不辣")]
    [InlineData(1, "微辣")]
    [InlineData(2, "小辣")]
    [InlineData(3, "中辣")]
    [InlineData(4, "重辣")]
    [InlineData(5, "变态辣")]
    public void SpicyLevel_应覆盖0到5档(short level, string expected)
    {
        Assert.Equal(expected, EnumLabels.SpicyLevel(level));
    }

    [Fact]
    public void SpicyLevel_越界值不应抛异常()
    {
        Assert.Equal("不辣", EnumLabels.SpicyLevel(-1));
        Assert.Equal("变态辣", EnumLabels.SpicyLevel(99));
    }

    [Fact]
    public void 每个枚举成员都应有中文标签()
    {
        foreach (var value in Enum.GetValues<Cuisine>())
        {
            Assert.False(string.IsNullOrWhiteSpace(EnumLabels.Cuisine(value)));
        }

        foreach (var value in Enum.GetValues<DishCategory>())
        {
            Assert.False(string.IsNullOrWhiteSpace(EnumLabels.DishCategory(value)));
        }

        foreach (var value in Enum.GetValues<DiningMode>())
        {
            Assert.False(string.IsNullOrWhiteSpace(EnumLabels.DiningMode(value)));
        }

        foreach (var value in Enum.GetValues<Platform>())
        {
            Assert.False(string.IsNullOrWhiteSpace(EnumLabels.Platform(value)));
        }

        foreach (var value in Enum.GetValues<RecordSource>())
        {
            Assert.False(string.IsNullOrWhiteSpace(EnumLabels.RecordSource(value)));
        }
    }
}

public class DiningModeWeightsTests
{
    [Fact]
    public void 默认权重应均分()
    {
        var weights = new DiningModeWeights();

        Assert.Equal(1.0 / 3.0, weights.Takeout, precision: 6);
        Assert.Equal(1.0 / 3.0, weights.DineIn, precision: 6);
        Assert.Equal(1.0 / 3.0, weights.Homemade, precision: 6);
    }

    [Fact]
    public void For_应返回对应方式的权重()
    {
        var weights = new DiningModeWeights { Takeout = 0.6, DineIn = 0.3, Homemade = 0.1 };

        Assert.Equal(0.6, weights.For(DiningMode.Takeout));
        Assert.Equal(0.3, weights.For(DiningMode.DineIn));
        Assert.Equal(0.1, weights.For(DiningMode.Homemade));
    }

    [Fact]
    public void For_随便模式应返回中性值()
    {
        var weights = new DiningModeWeights { Takeout = 1.0, DineIn = 0, Homemade = 0 };

        Assert.Equal(0.5, weights.For(DiningMode.Whatever));
    }

    [Fact]
    public void Set_应写入对应字段()
    {
        var weights = new DiningModeWeights();

        weights.Set(DiningMode.Takeout, 0.9);

        Assert.Equal(0.9, weights.Takeout);
    }
}

public class DishSnapshotTests
{
    [Fact]
    public void From_应复制菜品关键属性()
    {
        var dish = new Dish
        {
            Name = "番茄牛腩",
            Cuisine = Cuisine.Sichuan,
            Category = DishCategory.Meat,
            SpicyLevel = 1,
            Calories = 420,
            PriceMinCents = 3200,
            PriceMaxCents = 4800,
            Tags = ["下饭", "暖胃"],
        };

        var snapshot = DishSnapshot.From(dish);

        Assert.Equal(Cuisine.Sichuan, snapshot.Cuisine);
        Assert.Equal(DishCategory.Meat, snapshot.Category);
        Assert.Equal(1, snapshot.SpicyLevel);
        Assert.Equal(420, snapshot.CaloriesPerServing);
        Assert.Equal(4000, snapshot.PriceCents);   // 取区间中值
        Assert.Equal(["下饭", "暖胃"], snapshot.Tags);
    }

    [Fact]
    public void From_应取价格区间中值()
    {
        var dish = new Dish { PriceMinCents = 2000, PriceMaxCents = 3000 };
        Assert.Equal(2500, DishSnapshot.From(dish).PriceCents);

        // 只有下限
        var lowOnly = new Dish { PriceMinCents = 2000 };
        Assert.Equal(2000, DishSnapshot.From(lowOnly).PriceCents);

        // 只有上限
        var highOnly = new Dish { PriceMaxCents = 3000 };
        Assert.Equal(3000, DishSnapshot.From(highOnly).PriceCents);

        // 都没有
        var none = new Dish();
        Assert.Null(DishSnapshot.From(none).PriceCents);
    }

    [Fact]
    public void From_应复制标签而非共享引用()
    {
        var dish = new Dish { Tags = ["下饭"] };
        var snapshot = DishSnapshot.From(dish);

        dish.Tags.Add("重口");

        Assert.Single(snapshot.Tags);   // 快照不受后续修改影响
    }
}
