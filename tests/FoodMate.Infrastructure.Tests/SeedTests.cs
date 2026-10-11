using FoodMate.Core.Enums;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodMate.Infrastructure.Tests;

/// <summary>
/// 内置菜品种子数据的不变量。
/// </summary>
/// <remarks>
/// 这组测试是「种子文件的守门人」。在此之前没有任何测试覆盖种子加载器 ——
/// 意味着谁改一下 JSON，菜品数量、分类配比、菜谱步数都会静默回归，CI 全绿。
/// </remarks>
public class SeedTests : SqliteTestBase
{
    /// <summary>一次性加载全部种子（加载器自身会对孤儿菜谱抛异常）。</summary>
    private static readonly IReadOnlyList<Core.Entities.Dish> Dishes = DishSeedLoader.Load();

    [Fact]
    public void 内置菜品总数应不少于三百道()
    {
        Assert.True(
            Dishes.Count >= 300,
            $"内置菜品只有 {Dishes.Count} 道，低于 300 道的底线。");
    }

    [Fact]
    public void 菜名应唯一()
    {
        var duplicates = Dishes
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void 每道菜都应至少有两项食材()
    {
        var bad = Dishes
            .Where(d => d.Ingredients.Count < 2)
            .Select(d => $"{d.Name}({d.Ingredients.Count} 项)")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void 甜点与饮品分类应达标()
    {
        var dessert = Dishes.Count(d => d.Category == DishCategory.Dessert);
        var drink = Dishes.Count(d => d.Category == DishCategory.Drink);

        Assert.True(dessert >= 30, $"甜点只有 {dessert} 道，低于 30 道。");
        Assert.True(drink >= 25, $"饮品只有 {drink} 道，低于 25 道。");
    }

    [Fact]
    public void 至少四十道菜带三步以上菜谱()
    {
        var complete = Dishes.Count(d => d.Recipe is { Steps.Count: >= 3 });

        Assert.True(complete >= 40, $"只有 {complete} 道菜带 3 步以上的菜谱，低于 40 道。");
    }

    [Fact]
    public void 不应存在步数不足三步的菜谱()
    {
        var bad = Dishes
            .Where(d => d.Recipe is not null && d.Recipe.Steps.Count < 3)
            .Select(d => $"{d.Name}({d.Recipe!.Steps.Count} 步)")
            .ToList();

        Assert.Empty(bad);
    }

    [Fact]
    public void 每道菜都应有价格与热量()
    {
        var bad = Dishes
            .Where(d => d.PriceMinCents is null or <= 0
                        || d.PriceMaxCents is null or <= 0
                        || d.Calories is null or <= 0
                        || d.PriceMinCents > d.PriceMaxCents)
            .Select(d => d.Name)
            .ToList();

        Assert.Empty(bad);
    }

    /// <summary>
    /// O2 回归：已部署的库只增不改时，菜谱补充文件永远不会生效。
    /// </summary>
    /// <remarks>
    /// 这个场景在开发机上**看不出来** —— 开发库每次启动都重建，菜谱当然是对的。
    /// 但生产库（<c>RecreateOnStartup=false</c> + PostgreSQL）里，
    /// 老菜品的菜谱如果不做同步，就会永远停在旧版本，C3 在线上静默失败。
    /// </remarks>
    [Fact]
    public async Task 已有菜品缺菜谱时初始化器应把它补回来()
    {
        // 第一次初始化：建库 + 导入全部种子
        await using (var ctx = CreateContext())
        {
            await new DbInitializer(ctx, NullLogger<DbInitializer>.Instance).InitializeAsync();
        }

        string name;
        int originalSteps;

        // 模拟「已部署的旧库」：把某道菜的菜谱删掉
        await using (var ctx = CreateContext())
        {
            // Steps 是 JSON 列，没法在 SQL 里筛，先取回来再在内存里挑
            var candidates = await ctx.Dishes
                .Include(d => d.Recipe)
                .Where(d => d.IsBuiltin && d.Recipe != null)
                .ToListAsync();

            var dish = candidates.First(d => d.Recipe!.Steps.Count >= 3);

            name = dish.Name;
            originalSteps = dish.Recipe!.Steps.Count;

            ctx.Recipes.Remove(dish.Recipe!);
            await ctx.SaveChangesAsync();
        }

        // 第二次初始化：老实现「只增不改」，这里不会修好它
        await using (var ctx = CreateContext())
        {
            await new DbInitializer(ctx, NullLogger<DbInitializer>.Instance).InitializeAsync();
        }

        await using (var ctx = CreateContext())
        {
            var dish = await ctx.Dishes
                .Include(d => d.Recipe)
                .FirstAsync(d => d.Name == name);

            Assert.NotNull(dish.Recipe);
            Assert.Equal(originalSteps, dish.Recipe!.Steps.Count);
        }
    }
}
