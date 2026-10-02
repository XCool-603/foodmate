using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Infrastructure.Tests;

/// <summary>
/// 基于 SQLite 内存库的集成测试基类。
/// </summary>
/// <remarks>
/// <para>
/// 用真实 Provider 跑查询，能提前暴露「EF Core 翻译不了」这类只在运行时才炸的问题。
/// 每个测试拿到一个独立的连接与库，互不干扰。
/// </para>
/// <para>
/// ⚠️ 连接必须在 <see cref="DbContext"/> 存活期间保持打开，
/// 否则 SQLite 内存库会在连接关闭时被销毁。
/// </para>
/// </remarks>
public abstract class SqliteTestBase : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    /// <summary>测试用数据库上下文。</summary>
    protected FoodMateDbContext Db { get; private set; } = null!;

    /// <summary>创建一个新的上下文（用于验证跨上下文的读写）。</summary>
    protected FoodMateDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FoodMateDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new FoodMateDbContext(options);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        Db = CreateContext();
        await Db.Database.EnsureCreatedAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>插入一个用户及其默认画像。</summary>
    protected async Task<User> SeedUserAsync(short spicyLevel = 2, int budgetMin = 1500, int budgetMax = 5000)
    {
        var now = DateTimeOffset.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Nickname = "测试用户",
            CreatedAt = now,
            UpdatedAt = now,
        };

        Db.Users.Add(user);
        Db.UserPreferences.Add(new UserPreference
        {
            UserId = user.Id,
            SpicyLevel = spicyLevel,
            BudgetMinCents = budgetMin,
            BudgetMaxCents = budgetMax,
            UpdatedAt = now,
        });

        await Db.SaveChangesAsync();

        return user;
    }

    /// <summary>插入一道内置菜品。</summary>
    protected async Task<Dish> SeedDishAsync(
        string name,
        Cuisine cuisine = Cuisine.Other,
        DishCategory category = DishCategory.Meat,
        short spicyLevel = 0,
        int priceMin = 2000,
        int priceMax = 3000,
        int popularity = 50,
        bool withRecipe = false,
        Ingredient[]? ingredients = null,
        string[]? tags = null,
        string[]? aliases = null,
        MealTimeMask mealTimes = MealTimeMask.All)
    {
        var now = DateTimeOffset.UtcNow;

        var dish = new Dish
        {
            Id = Guid.NewGuid(),
            Name = name,
            Aliases = [.. aliases ?? []],
            Cuisine = cuisine,
            Category = category,
            SpicyLevel = spicyLevel,
            PriceMinCents = priceMin,
            PriceMaxCents = priceMax,
            Popularity = popularity,
            Ingredients = [.. ingredients ?? []],
            Tags = [.. tags ?? []],
            MealTimes = mealTimes,
            IsBuiltin = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (withRecipe)
        {
            dish.Recipe = new Recipe
            {
                Id = Guid.NewGuid(),
                DishId = dish.Id,
                CookMinutes = 15,
                Difficulty = 1,
                Steps = [new RecipeStep(1, "随便炒炒")],
                CreatedAt = now,
            };
        }

        Db.Dishes.Add(dish);
        await Db.SaveChangesAsync();

        return dish;
    }

    /// <summary>插入一条饮食记录，并同步统计表。</summary>
    protected async Task<MealRecord> SeedRecordAsync(
        Guid userId,
        Guid dishId,
        string dishName,
        DateTimeOffset eatenAt,
        double servings = 1.0,
        short? rating = null,
        bool? wouldEatAgain = null,
        Cuisine cuisine = Cuisine.Other)
    {
        var record = new MealRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DishId = dishId,
            DishName = dishName,
            DishSnapshot = new DishSnapshot { Cuisine = cuisine },
            MealType = MealType.Lunch,
            DiningMode = DiningMode.DineIn,
            EatenAt = eatenAt,
            Servings = servings,
            Rating = rating,
            WouldEatAgain = wouldEatAgain,
            Source = RecordSource.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        Db.MealRecords.Add(record);
        await Db.SaveChangesAsync();

        return record;
    }
}
