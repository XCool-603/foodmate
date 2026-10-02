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

        var existingNames = await db.Dishes
            .AsNoTracking()
            .Where(d => d.IsBuiltin)
            .Select(d => d.Name)
            .ToListAsync(ct);

        var existing = new HashSet<string>(existingNames, StringComparer.Ordinal);

        var missing = seedDishes.Where(d => !existing.Contains(d.Name)).ToList();

        if (missing.Count == 0)
        {
            logger.LogInformation("内置菜品已是最新，共 {Count} 道。", existingNames.Count);
            return;
        }

        db.Dishes.AddRange(missing);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "已导入内置菜品 {Added} 道（原有 {Existing} 道，种子总数 {Total} 道）。",
            missing.Count,
            existingNames.Count,
            seedDishes.Count);
    }
}
