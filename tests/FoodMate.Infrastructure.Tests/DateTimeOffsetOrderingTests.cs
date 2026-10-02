using FoodMate.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Infrastructure.Tests;

/// <summary>
/// 验证 SQLite 下 <see cref="DateTimeOffset"/> 的<b>存储格式确实保持时间序</b>。
/// </summary>
/// <remarks>
/// 前面的翻译测试只证明「查询能跑」，不证明「结果对」。
/// 字符串比较要等价于时间比较，格式必须定宽且统一 UTC——
/// 这组测试就是守这条不变量的。
/// </remarks>
public class DateTimeOffsetOrderingTests : SqliteTestBase
{
    /// <summary>构造一个固定的基准时刻，避免测试依赖当前时间。</summary>
    private static readonly DateTimeOffset Base =
        new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task 不同偏移量的同一时刻应存为同一时间点()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        // 同一时刻的三种表示
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base);                                  // UTC
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base.ToOffset(TimeSpan.FromHours(8)));   // 北京时间
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base.ToOffset(TimeSpan.FromHours(-5)));  // 美东

        var exact = await Db.MealRecords
            .AsNoTracking()
            .Where(r => r.EatenAt == Base)
            .CountAsync();

        Assert.Equal(3, exact);
    }

    [Fact]
    public async Task 时间范围过滤应返回正确条数()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base.AddDays(-1));
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base.AddDays(-10));
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base.AddDays(-20));
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base.AddDays(-40));
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", Base.AddDays(-100));

        var within30 = await Db.MealRecords
            .AsNoTracking()
            .CountAsync(r => r.UserId == user.Id && r.EatenAt >= Base.AddDays(-30));

        Assert.Equal(3, within30);

        var within7 = await Db.MealRecords
            .AsNoTracking()
            .CountAsync(r => r.UserId == user.Id && r.EatenAt >= Base.AddDays(-7));

        Assert.Equal(1, within7);
    }

    [Fact]
    public async Task 排序应与时间顺序一致()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        // 刻意用带不同偏移量的时间点，验证字典序仍等于时间序
        var times = new[]
        {
            Base.AddDays(-30),
            Base.AddDays(-1).ToOffset(TimeSpan.FromHours(8)),
            Base.AddDays(-7).ToOffset(TimeSpan.FromHours(-5)),
            Base,
            Base.AddDays(-14),
        };

        foreach (var time in times)
        {
            await SeedRecordAsync(user.Id, dish.Id, "测试菜", time);
        }

        var ordered = await Db.MealRecords
            .AsNoTracking()
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.EatenAt)
            .Select(r => r.EatenAt)
            .ToListAsync();

        var expected = times
            .Select(t => t.ToUniversalTime())
            .OrderByDescending(t => t)
            .ToList();

        Assert.Equal(expected, ordered.Select(t => t.ToUniversalTime()).ToList());
    }

    [Fact]
    public async Task 毫秒级差异应能正确区分()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var earlier = Base.AddMilliseconds(-1);
        var later = Base.AddMilliseconds(1);

        await SeedRecordAsync(user.Id, dish.Id, "测试菜", earlier);
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", later);

        var count = await Db.MealRecords
            .AsNoTracking()
            .CountAsync(r => r.UserId == user.Id && r.EatenAt > earlier);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task 往返后应保持UTC且时刻不变()
    {
        var user = await SeedUserAsync();
        var dish = await SeedDishAsync("测试菜");

        var original = Base.ToOffset(TimeSpan.FromHours(8));
        await SeedRecordAsync(user.Id, dish.Id, "测试菜", original);

        await using var fresh = CreateContext();
        var loaded = await fresh.MealRecords.AsNoTracking().FirstAsync(r => r.UserId == user.Id);

        Assert.Equal(original.ToUniversalTime(), loaded.EatenAt.ToUniversalTime());
        Assert.Equal(TimeSpan.Zero, loaded.EatenAt.Offset);
    }
}
