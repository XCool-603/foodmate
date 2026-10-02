using FoodMate.Contracts.Dishes;
using FoodMate.Core;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FoodMate.Api.Endpoints;

/// <summary>菜品端点。</summary>
/// <remarks>
/// M0 阶段只实现列表与详情，筛选维度将在 M1 补齐。
/// 搜索在应用层内存中完成（含别名匹配），这在菜品量 <c>2000</c> 以内比数据库模糊查询
/// 更快也更灵活；数据量再大时改用全文检索。
/// </remarks>
public static class DishEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapDishEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dishes", async (
            FoodMateDbContext db,
            string? keyword,
            short? cuisine,
            short? category,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

            var all = await db.Dishes
                .AsNoTracking()
                .Include(d => d.Recipe)
                .Where(d => d.IsActive && !d.IsDeleted)
                .ToListAsync(ct);

            IEnumerable<Dish> query = all;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var k = keyword.Trim();
                query = query.Where(d =>
                    d.Name.Contains(k, StringComparison.OrdinalIgnoreCase)
                    || d.Aliases.Any(a => a.Contains(k, StringComparison.OrdinalIgnoreCase)));
            }

            if (cuisine is { } c)
            {
                query = query.Where(d => (short)d.Cuisine == c);
            }

            if (category is { } cat)
            {
                query = query.Where(d => (short)d.Category == cat);
            }

            var ordered = query
                .OrderByDescending(d => d.Popularity)
                .ThenBy(d => d.Name, StringComparer.Ordinal)
                .ToList();

            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(DishBriefDto.From)
                .ToList();

            return PagedResult<DishBriefDto>.Create(items, ordered.Count, page, pageSize);
        })
        .WithName("ListDishes")
        .WithSummary("菜品列表")
        .WithTags("菜品");

        app.MapGet("/dishes/{id:guid}", async (
            Guid id,
            FoodMateDbContext db,
            CancellationToken ct) =>
        {
            var dish = await db.Dishes
                .AsNoTracking()
                .Include(d => d.Recipe)
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);

            if (dish is null)
            {
                throw Core.Exceptions.BusinessException.NotFound($"菜品 {id} 不存在");
            }

            return DishBriefDto.From(dish);
        })
        .WithName("GetDish")
        .WithSummary("菜品详情")
        .WithTags("菜品");

        return app;
    }
}
