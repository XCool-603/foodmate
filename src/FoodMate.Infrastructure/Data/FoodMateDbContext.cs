using System.Globalization;
using FoodMate.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FoodMate.Infrastructure.Data;

/// <summary>美食伴侣数据库上下文。</summary>
public sealed class FoodMateDbContext(DbContextOptions<FoodMateDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    public DbSet<Dish> Dishes => Set<Dish>();

    public DbSet<Recipe> Recipes => Set<Recipe>();

    public DbSet<MealRecord> MealRecords => Set<MealRecord>();

    public DbSet<DecisionSession> DecisionSessions => Set<DecisionSession>();

    public DbSet<AiRecognitionLog> AiRecognitionLogs => Set<AiRecognitionLog>();

    public DbSet<UserDishStat> UserDishStats => Set<UserDishStat>();

    public DbSet<UserCuisineStat> UserCuisineStats => Set<UserCuisineStat>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FoodMateDbContext).Assembly);
        modelBuilder.ApplySnakeCaseNames();

        if (Database.IsSqlite())
        {
            ApplySqliteDateTimeOffsetWorkaround(modelBuilder);
        }
    }

    /// <summary>
    /// SQLite 下把 <see cref="DateTimeOffset"/> 存为<b>可排序的 ISO-8601 UTC 字符串</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么需要这个：</b>EF Core 的 SQLite Provider <b>不支持 <see cref="DateTimeOffset"/>
    /// 的比较与排序</b>——<c>Where(r =&gt; r.EatenAt &gt;= since)</c> 会在运行时抛
    /// 「could not be translated」。而「取某用户近 30 天的记录」正是决策引擎
    /// 计算新鲜度的核心查询，绕不过去。
    /// </para>
    /// <para>
    /// <b>为什么这样能解决：</b>把 Provider 类型换成 <c>string</c> 后，
    /// EF Core 会按字符串比较来翻译 <c>&gt;=</c> / <c>OrderBy</c>。
    /// 只要格式是<b>定宽且统一 UTC</b>，字典序就等于时间序，结果正确。
    /// </para>
    /// <para>
    /// <b>为什么不直接改用 <c>DateTime</c>：</b>那会丢掉偏移量语义，
    /// 而且需要在实体、引擎、DTO、测试里做一次大范围机械改写。
    /// 这里的做法把差异<b>完全限制在持久化层</b>，
    /// 生产环境的 PostgreSQL 仍然映射为原生 <c>timestamptz</c>。
    /// </para>
    /// <para>
    /// 该格式与 EF Core 默认的 <c>yyyy-MM-dd HH:mm:ss.fffffff+HH:mm</c> 不同——
    /// 后者在<b>偏移量不一致时字典序会错乱</b>，因此这里强制转 UTC 并定宽输出。
    /// </para>
    /// </remarks>
    private static void ApplySqliteDateTimeOffsetWorkaround(ModelBuilder modelBuilder)
    {
        const string format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        var converter = new ValueConverter<DateTimeOffset, string>(
            value => value.ToUniversalTime().ToString(format, CultureInfo.InvariantCulture),
            value => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset)
                    || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(converter);
                }
            }
        }
    }
}
