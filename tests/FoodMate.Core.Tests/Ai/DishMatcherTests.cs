using FoodMate.Core.Ai;

namespace FoodMate.Core.Tests.Ai;

public class DishMatcherTests
{
    private static readonly Guid TomatoBeefId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid KungPaoId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TomatoEggId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly List<DishMatchTarget> Library =
    [
        new(TomatoBeefId, "番茄牛腩", ["西红柿炖牛腩", "番茄炖牛肉"]),
        new(KungPaoId, "宫保鸡丁", ["宫爆鸡丁"]),
        new(TomatoEggId, "西红柿炒蛋", ["番茄炒蛋", "西红柿炒鸡蛋"]),
    ];

    [Fact]
    public void 正式名完全一致应满分匹配()
    {
        var match = DishMatcher.Match("番茄牛腩", Library);

        Assert.NotNull(match);
        Assert.Equal(TomatoBeefId, match.DishId);
        Assert.Equal(1.0, match.Score, precision: 3);
        Assert.False(match.ViaAlias);
    }

    [Theory]
    [InlineData("西红柿炖牛腩")]
    [InlineData("番茄炖牛肉")]
    public void 别名一致应高分匹配并标记来源(string alias)
    {
        var match = DishMatcher.Match(alias, Library);

        Assert.NotNull(match);
        Assert.Equal(TomatoBeefId, match.DishId);
        Assert.True(match.ViaAlias);
        Assert.True(match.Score >= 0.9, $"别名匹配分数偏低：{match.Score}");
    }

    [Theory]
    [InlineData("番茄炒蛋")]
    [InlineData("番茄炒鸡蛋")]
    [InlineData("西红柿炒鸡蛋")]
    public void 常见别名写法都应命中(string name)
    {
        var match = DishMatcher.Match(name, Library);

        Assert.NotNull(match);
        Assert.Equal(TomatoEggId, match.DishId);
    }

    [Theory]
    [InlineData("宫爆鸡丁")]
    [InlineData("宫保鸡丁（微辣）")]
    [InlineData(" 宫保鸡丁 ")]
    public void 标点空格与括号不应影响匹配(string name)
    {
        var match = DishMatcher.Match(name, Library);

        Assert.NotNull(match);
        Assert.Equal(KungPaoId, match.DishId);
    }

    [Fact]
    public void 归一化应去掉空白与标点()
    {
        Assert.Equal("宫保鸡丁", DishMatcher.Normalize("宫保 鸡丁"));
        Assert.Equal("宫保鸡丁", DishMatcher.Normalize("宫保·鸡丁"));
        Assert.Equal("番茄炒蛋", DishMatcher.Normalize("番茄（炒）蛋"));
        Assert.Equal("abc", DishMatcher.Normalize(" A B C "));
    }

    [Fact]
    public void 完全无关的菜名不应匹配()
    {
        Assert.Null(DishMatcher.Match("法式鹅肝", Library));
    }

    [Fact]
    public void 过短的菜名不应造成误匹配()
    {
        // 「饭」只有 1 个字，不该匹配上「蛋炒饭」这类菜
        Assert.Null(DishMatcher.Match("饭", Library));
    }

    [Fact]
    public void 包含关系应给出合理分数()
    {
        var library = new List<DishMatchTarget>
        {
            new(Guid.NewGuid(), "番茄牛腩煲", []),
        };

        var match = DishMatcher.Match("番茄牛腩", library);

        Assert.NotNull(match);
        Assert.InRange(match.Score, 0.6, 0.95);
    }

    [Fact]
    public void 空输入应返回null()
    {
        Assert.Null(DishMatcher.Match("", Library));
        Assert.Null(DishMatcher.Match("   ", Library));
        Assert.Null(DishMatcher.Match("番茄牛腩", []));
    }

    [Fact]
    public void 批量匹配应返回序号到结果的映射()
    {
        var names = new[] { "番茄牛腩", "不存在的菜", "宫爆鸡丁" };

        var matches = DishMatcher.MatchAll(names, Library);

        Assert.Equal(2, matches.Count);
        Assert.Equal(TomatoBeefId, matches[0].DishId);
        Assert.Equal(KungPaoId, matches[2].DishId);
        Assert.False(matches.ContainsKey(1));
    }

    [Fact]
    public void 批量匹配不应把同一道菜分配给多个条目()
    {
        // 一顿饭里同一道菜不会出现两次，重复命中应让位
        var names = new[] { "番茄牛腩", "西红柿炖牛腩" };

        var matches = DishMatcher.MatchAll(names, Library);

        Assert.Single(matches);
        Assert.Equal(TomatoBeefId, matches[0].DishId);
    }

    [Fact]
    public void 应优先选择分数最高的候选()
    {
        var library = new List<DishMatchTarget>
        {
            new(Guid.NewGuid(), "番茄", []),
            new(Guid.NewGuid(), "番茄牛腩", []),
        };

        var match = DishMatcher.Match("番茄牛腩", library);

        Assert.NotNull(match);
        Assert.Equal("番茄牛腩", match.MatchedName);
        Assert.Equal(1.0, match.Score, precision: 3);
    }
}

public class RecognizedDishItemTests
{
    [Theory]
    [InlineData(0.95, false)]
    [InlineData(0.70, false)]
    [InlineData(0.69, true)]
    [InlineData(0.30, true)]
    public void 低置信度应要求复核(double confidence, bool expected)
    {
        var item = new RecognizedDishItem("测试菜", confidence);

        Assert.Equal(expected, item.NeedsReview);
    }
}
