using System.Text;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Infrastructure.Data;

/// <summary>
/// 把 EF Core 模型中的表名与列名统一转换为 <c>snake_case</c>，
/// 使数据库结构与设计文档中的 DDL 保持一致，同时免去每个实体上的逐列命名样板代码。
/// </summary>
internal static class SnakeCaseConvention
{
    /// <summary>对所有实体的表名与列名应用 snake_case 命名。</summary>
    public static void ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (!string.IsNullOrEmpty(tableName))
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName();
                if (!string.IsNullOrEmpty(columnName))
                {
                    property.SetColumnName(ToSnakeCase(columnName));
                }
            }
        }
    }

    /// <summary>把 PascalCase / camelCase 标识符转换为 snake_case。</summary>
    /// <example><c>AvatarUrl</c> → <c>avatar_url</c>，<c>DishId</c> → <c>dish_id</c>。</example>
    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length + 8);

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (char.IsUpper(c))
            {
                var prevIsLowerOrDigit = i > 0 && !char.IsUpper(input[i - 1]);
                var nextIsLower = i + 1 < input.Length && char.IsLower(input[i + 1]);

                if (i > 0 && (prevIsLowerOrDigit || nextIsLower))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
