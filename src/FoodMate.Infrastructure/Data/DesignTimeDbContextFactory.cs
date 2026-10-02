using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FoodMate.Infrastructure.Data;

/// <summary>
/// 设计时上下文工厂。
/// </summary>
/// <remarks>
/// <para>
/// 让 <c>dotnet ef</c> 命令不必启动整个应用就能构造 <see cref="FoodMateDbContext"/>。
/// 否则设计时工具会走 <c>Program.cs</c> 的宿主构建流程，
/// 而那里有「启动即建表 + 导种子数据」的逻辑——在生成迁移时触发它是危险的。
/// </para>
/// <para>
/// Provider 与连接串从环境变量读取，默认按 <b>PostgreSQL</b> 生成迁移
/// （生产用的就是这个，SQLite 开发库走 <c>EnsureCreated</c> 不需要迁移）。
/// </para>
/// <example>
/// <code>
/// dotnet ef migrations add InitialCreate -p src/FoodMate.Infrastructure -s src/FoodMate.Api
/// </code>
/// </example>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FoodMateDbContext>
{
    public FoodMateDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Database__Provider") ?? "PostgreSql";

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=foodmate;Username=foodmate;Password=foodmate";

        var builder = new DbContextOptionsBuilder<FoodMateDbContext>();

        if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseNpgsql(connectionString);
        }
        else
        {
            builder.UseSqlite(connectionString);
        }

        return new FoodMateDbContext(builder.Options);
    }
}
