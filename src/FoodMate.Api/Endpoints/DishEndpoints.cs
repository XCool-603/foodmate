using FoodMate.Contracts.Dishes;
using FoodMate.Core;
using FoodMate.Core.Decision;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using FoodMate.Infrastructure.Data;
using FoodMate.Infrastructure.Identity;
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
            HttpContext http,
            FoodMateDbContext db,
            GuestUserService users,
            CancellationToken ct) =>
        {
            var userId = await CurrentUser.ResolveAsync(http, users, ct);

            var dish = await db.Dishes
                .AsNoTracking()
                .Include(d => d.Recipe)
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);

            if (dish is null)
            {
                throw Core.Exceptions.BusinessException.NotFound($"菜品 {id} 不存在");
            }

            return await ToDetailAsync(db, dish, userId, ct);
        })
        .WithName("GetDish")
        .WithSummary("菜品详情（含食材与菜谱）")
        .WithDescription(
            "「自己做」模式下用户需要知道用什么、怎么做，因此详情比列表多出食材与菜谱。"
            + "菜品库未缓存菜谱时 recipe 为 null，前端可调 POST /ai/recipe 生成。")
        .WithTags("菜品");

        return app;
    }

    /// <summary>投影为详情 DTO，并附上与该用户忌口的冲突提示。</summary>
    private static async Task<DishDetailDto> ToDetailAsync(
        FoodMateDbContext db,
        Dish dish,
        Guid userId,
        CancellationToken ct)
    {
        var preference = await db.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        var conflicts = new List<string>();

        if (preference is { AvoidIngredients.Count: > 0 })
        {
            var expanded = HardFilter.ExpandAvoidList(preference.AvoidIngredients);

            // 复用决策引擎的忌口匹配规则（含双向包含与同义词展开），
            // 保证「详情页提示」与「推荐过滤」口径一致
            conflicts = [.. dish.Ingredients
                .Where(ing => expanded.Any(a =>
                    ing.Name.Contains(a, StringComparison.OrdinalIgnoreCase)
                    || (ing.Name.Length >= 2 && a.Contains(ing.Name, StringComparison.OrdinalIgnoreCase))))
                .Select(ing => ing.Name)
                .Distinct()];
        }

        return new DishDetailDto
        {
            Id = dish.Id,
            Name = dish.Name,
            Aliases = [.. dish.Aliases],
            Cuisine = (short)dish.Cuisine,
            CuisineLabel = EnumLabels.Cuisine(dish.Cuisine),
            Category = (short)dish.Category,
            CategoryLabel = EnumLabels.DishCategory(dish.Category),
            SpicyLevel = dish.SpicyLevel,
            SpicyLabel = EnumLabels.SpicyLevel(dish.SpicyLevel),
            PriceMinCents = dish.PriceMinCents,
            PriceMaxCents = dish.PriceMaxCents,
            Calories = dish.Calories,
            ImageUrl = dish.ImageUrl,
            Description = dish.Description,
            Tags = [.. dish.Tags],
            MealTimeLabels = [.. MealTimeLabelsOf(dish.MealTimes)],
            Ingredients = [.. dish.Ingredients.Select(i => new DishIngredientDto(i.Name, i.Amount, i.IsCommonAllergen))],
            Recipe = dish.Recipe is null ? null : new DishRecipeDto
            {
                Servings = dish.Recipe.Servings,
                CookMinutes = dish.Recipe.CookMinutes,
                Difficulty = dish.Recipe.Difficulty,
                DifficultyLabel = EnumLabels.Difficulty(dish.Recipe.Difficulty),
                Steps = [.. dish.Recipe.Steps
                    .OrderBy(s => s.Order)
                    .Select(s => new DishRecipeStepDto(s.Order, s.Text, s.DurationMinutes))],
                Tips = dish.Recipe.Tips,
            },
            IsBuiltin = dish.IsBuiltin,
            ConflictIngredients = conflicts,
        };
    }

    /// <summary>把餐次位掩码展开成中文名称列表。</summary>
    private static IEnumerable<string> MealTimeLabelsOf(MealTimeMask mask)
    {
        foreach (var mealType in Enum.GetValues<MealType>())
        {
            if (mask.HasFlag(MealTimes.ToMask(mealType)))
            {
                yield return MealTimes.Label(mealType);
            }
        }
    }
}
