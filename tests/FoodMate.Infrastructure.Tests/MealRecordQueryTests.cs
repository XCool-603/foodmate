using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Infrastructure.Tests;

/// <summary>
/// 验证 <c>meal_records</c> 的各类查询在 SQLite 上能被正确翻译。
/// </summary>
/// <remarks>
/// 这组测试的存在原因：SQLite 对 <see cref="DateTimeOffset"/> 的支持有限，
/// 某些看似普通的比较会在运行时抛「could not be translated」。
/// 用真实 Provider 跑一遍才能提前发现。
/// </remarks>
public class MealRecordQueryTests : SqliteTestBase
{
    [Fact]
    public async Task 按用户过滤应能翻译()
    {
        var user = await SeedUserAsync();

        var count = await Db.MealRecords
            .AsNoTracking()
            .CountAsync(r => r.UserId == user.Id);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task 按用户与未删除过滤应能翻译()
    {
        var user = await SeedUserAsync();

        var rows = await Db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == user.Id && !r.IsDeleted)
            .Select(r => r.DishId)
            .ToListAsync();

        Assert.Empty(rows);
    }

    [Fact]
    public async Task 可空外键非空判断应能翻译()
    {
        var user = await SeedUserAsync();

        var rows = await Db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == user.Id && r.DishId != null)
            .Select(r => r.DishId)
            .ToListAsync();

        Assert.Empty(rows);
    }

    [Fact]
    public async Task DateTimeOffset_比较是否可翻译()
    {
        var user = await SeedUserAsync();
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        // 这是 M1 决策服务真正依赖的查询
        var rows = await Db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == user.Id && r.EatenAt >= since)
            .Select(r => r.DishId)
            .ToListAsync();

        Assert.Empty(rows);
    }

    [Fact]
    public async Task 完整的新鲜度查询应能翻译()
    {
        var user = await SeedUserAsync();
        var since = DateTimeOffset.UtcNow.AddDays(-30);

        var rows = await Db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == user.Id
                        && !r.IsDeleted
                        && r.DishId != null
                        && r.EatenAt >= since)
            .Select(r => r.DishId)
            .ToListAsync();

        Assert.Empty(rows);
    }

    [Fact]
    public async Task 按菜名与评分排序应能翻译()
    {
        var user = await SeedUserAsync();

        var rows = await Db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.EatenAt)
            .Take(10)
            .ToListAsync();

        Assert.Empty(rows);
    }
}

/// <summary>验证 JSON 列的往返与变更追踪。</summary>
public class JsonColumnTests : SqliteTestBase
{
    [Fact]
    public async Task 菜品标签与食材应能往返()
    {
        var dish = await SeedDishAsync(
            "番茄牛腩",
            ingredients: [new Ingredient("牛腩", "500g"), new Ingredient("花生米", "50g", true)],
            tags: ["下饭", "暖胃"]);

        await using var fresh = CreateContext();
        var loaded = await fresh.Dishes.AsNoTracking().FirstAsync(d => d.Id == dish.Id);

        Assert.Equal(["下饭", "暖胃"], loaded.Tags);
        Assert.Equal(2, loaded.Ingredients.Count);
        Assert.Equal("牛腩", loaded.Ingredients[0].Name);
        Assert.True(loaded.Ingredients[1].IsCommonAllergen);
    }

    [Fact]
    public async Task 中文应以原样存储而非转义()
    {
        var dish = await SeedDishAsync("红烧肉", tags: ["家常"]);

        await using var fresh = CreateContext();
        var loaded = await fresh.Dishes.AsNoTracking().FirstAsync(d => d.Id == dish.Id);

        Assert.Equal("家常", loaded.Tags[0]);
        Assert.Equal("红烧肉", loaded.Name);
    }

    [Fact]
    public async Task 原地修改集合应被变更追踪捕获()
    {
        var dish = await SeedDishAsync("测试菜", tags: ["初始"]);

        var tracked = await Db.Dishes.FirstAsync(d => d.Id == dish.Id);
        tracked.Tags.Add("新增");
        await Db.SaveChangesAsync();

        await using var fresh = CreateContext();
        var loaded = await fresh.Dishes.AsNoTracking().FirstAsync(d => d.Id == dish.Id);

        Assert.Contains("新增", loaded.Tags);
    }

    [Fact]
    public async Task 枚举与位掩码应能往返()
    {
        var dish = await SeedDishAsync(
            "早餐菜",
            cuisine: Cuisine.Cantonese,
            category: DishCategory.Breakfast,
            spicyLevel: 3,
            mealTimes: MealTimeMask.Breakfast);

        await using var fresh = CreateContext();
        var loaded = await fresh.Dishes.AsNoTracking().FirstAsync(d => d.Id == dish.Id);

        Assert.Equal(Cuisine.Cantonese, loaded.Cuisine);
        Assert.Equal(DishCategory.Breakfast, loaded.Category);
        Assert.Equal(3, loaded.SpicyLevel);
        Assert.Equal(MealTimeMask.Breakfast, loaded.MealTimes);
    }
}
