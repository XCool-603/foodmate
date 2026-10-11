using FoodMate.Core.Entities;
using FoodMate.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodMate.Infrastructure.Data;

/// <summary>
/// 数据库初始化：应用迁移（或开发环境建表）+ 导入内置菜品种子数据。
/// </summary>
/// <remarks>
/// <para>
/// <b>迁移策略</b>（见 ADR-001）：
/// SQLite 开发库使用 <c>EnsureCreated</c>，PostgreSQL 生产库使用正式迁移。
/// 这样避免维护两套迁移脚本。
/// </para>
/// <para>
/// <b>种子策略</b>：按菜名增量导入——已存在的不动，缺失的补上。
/// 既保证幂等，又能在扩展种子数据后让已有开发库自动补齐。
/// </para>
/// </remarks>
public sealed class DbInitializer(
    FoodMateDbContext db,
    ILogger<DbInitializer> logger)
{
    /// <summary>建表并导入种子数据。</summary>
    /// <param name="recreate">
    /// 是否先删除再重建数据库。<b>仅用于开发环境</b>——SQLite 的
    /// <c>EnsureCreated</c> 不会更新已存在的库，模型变更后需要重建。
    /// 生产环境必须为 <c>false</c>。
    /// </param>
    /// <param name="ct">取消令牌。</param>
    public async Task InitializeAsync(bool recreate = false, CancellationToken ct = default)
    {
        if (recreate)
        {
            logger.LogWarning("Database:RecreateOnStartup = true —— 正在删除并重建数据库（仅限开发环境）。");
            await db.Database.EnsureDeletedAsync(ct);
        }

        await EnsureSchemaAsync(ct);
        await SeedDishesAsync(ct);
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            logger.LogInformation("PostgreSQL：应用迁移…");
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            logger.LogInformation("SQLite：确保表结构存在…");
            await db.Database.EnsureCreatedAsync(ct);
        }
    }

    private async Task SeedDishesAsync(CancellationToken ct)
    {
        var seedDishes = DishSeedLoader.Load();

        // 注意：Steps 是序列化到 recipes.steps 的 JSON 列（值对象），不是导航属性，
        // 所以**不能**写 ThenInclude —— EF 会因为表达式不是属性访问而抛异常。
        // Include 了 Recipe 就等于把步骤一起取回来了。
        var existing = await db.Dishes
            .Where(d => d.IsBuiltin)
            .Include(d => d.Recipe)
            .ToListAsync(ct);

        var byName = existing.ToDictionary(d => d.Name, StringComparer.Ordinal);

        var added = 0;
        var recipesSynced = 0;

        foreach (var seed in seedDishes)
        {
            if (!byName.TryGetValue(seed.Name, out var current))
            {
                db.Dishes.Add(seed);
                added++;
                continue;
            }

            // 已有菜品：**只同步菜谱**，不动用户可见的属性。
            //
            // 为什么必须做这一步：种子里的菜谱是权威版本，而
            // `dishes.seed.recipes.json` 这种补充文件的存在意义就是
            // 「补齐并修正」已有菜谱（例如把只有 2 步的补到 3 步）。
            // 若这里只增不改，新建的开发库能生效，**已部署的库永远拿不到修正**——
            // 菜谱功能会在生产环境静默缺失。
            if (seed.Recipe is null || RecipeMatches(current.Recipe, seed.Recipe))
            {
                continue;
            }

            if (current.Recipe is not null)
            {
                db.Recipes.Remove(current.Recipe);
            }

            // 种子里这道菜的 Recipe.DishId 指向的是种子自己的临时 Guid，
            // 必须改指到库里这道菜的 Id，否则外键会指向一个不存在的菜品。
            seed.Recipe.DishId = current.Id;
            current.Recipe = seed.Recipe;

            // 必须显式 Add：Recipe.Id 是 Guid 且配置为 ValueGeneratedOnAdd，
            // 种子已经赋了非空值，EF 会据此认为「这是一行已存在的数据」，
            // 于是发 UPDATE 而不是 INSERT，最终抛
            // DbUpdateConcurrencyException（expected to affect 1 row, affected 0）。
            db.Recipes.Add(seed.Recipe);

            recipesSynced++;
        }

        if (added == 0 && recipesSynced == 0)
        {
            logger.LogInformation("内置菜品已是最新，共 {Count} 道。", existing.Count);
            return;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "内置菜品同步完成：新增 {Added} 道、更新菜谱 {Recipes} 份（原有 {Existing} 道，种子总数 {Total} 道）。",
            added,
            recipesSynced,
            existing.Count,
            seedDishes.Count);
    }

    /// <summary>菜谱是否与种子完全一致（比份数、时长与每步文本）。</summary>
    private static bool RecipeMatches(Recipe? current, Recipe seed)
    {
        if (current is null)
        {
            return false;
        }

        if (current.Servings != seed.Servings || current.CookMinutes != seed.CookMinutes)
        {
            return false;
        }

        var a = current.Steps.OrderBy(s => s.Order).ToList();
        var b = seed.Steps.OrderBy(s => s.Order).ToList();

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Text, b[i].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
