using FoodMate.Core.Entities;
using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision;

/// <summary>
/// 硬过滤：把<b>不合法或安全相关</b>的候选直接移出集合。
/// </summary>
/// <remarks>
/// <para>
/// 硬过滤与打分的分工必须严格区分：
/// </para>
/// <list type="bullet">
///   <item><b>硬过滤</b>只处理「这道菜能不能出现在用户面前」——下架、他人私有、含忌口。</item>
///   <item><b>偏好</b>（时段、距离、预算）一律走打分，不用过滤。分数低仍可能被选中，
///         但那只是「不太合适」，不是「不能吃」。</item>
/// </list>
/// <para>
/// ⚠️ <b>忌口与过敏原是安全底线，任何降级层级都不放开。</b>
/// 分数低仍可能被转盘抽中，这是安全问题，不能妥协。
/// </para>
/// </remarks>
public static class HardFilter
{
    /// <summary>
    /// 应用硬过滤。
    /// </summary>
    /// <param name="context">决策上下文。</param>
    /// <param name="enforceDiningMode">
    /// 是否强制就餐方式约束（「自己做」必须有菜谱）。
    /// 候选不足时引擎会以 <c>false</c> 重试，但<b>忌口过滤永不放开</b>。
    /// </param>
    public static IReadOnlyList<DishCandidate> Apply(
        DecisionContext context,
        bool enforceDiningMode = true)
    {
        var expandedAvoid = ExpandAvoidList(context.Preference.AvoidIngredients);
        var diningMode = context.Request.DiningMode;

        var result = new List<DishCandidate>(context.Candidates.Count);

        foreach (var dish in context.Candidates)
        {
            // F3 / F4：忌口与过敏原 —— 安全底线，永不放开
            if (expandedAvoid.Count > 0 && ContainsAvoided(dish, expandedAvoid))
            {
                continue;
            }

            // F5：自己做模式必须有菜谱
            if (enforceDiningMode
                && diningMode == DiningMode.Homemade
                && !dish.HasRecipe)
            {
                continue;
            }

            result.Add(dish);
        }

        return result;
    }

    /// <summary>
    /// 展开忌口词：把「坚果」这类上位词展开为其同义词集合。
    /// </summary>
    /// <remarks>
    /// 用户写「坚果」，菜品写「腰果」——不展开就匹配不上。
    /// </remarks>
    public static IReadOnlySet<string> ExpandAvoidList(IReadOnlyList<string> avoidIngredients)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in avoidIngredients)
        {
            var term = raw?.Trim();
            if (string.IsNullOrEmpty(term))
            {
                continue;
            }

            expanded.Add(term);

            if (AllergenSynonyms.TryGetValue(term, out var synonyms))
            {
                foreach (var synonym in synonyms)
                {
                    expanded.Add(synonym);
                }
            }
        }

        return expanded;
    }

    /// <summary>
    /// 判断菜品是否含有被忌口的食材。
    /// </summary>
    /// <remarks>
    /// 采用<b>双向包含</b>匹配：用户写「花生」能匹配菜品的「花生米」，
    /// 用户写「花生米」也能匹配菜品的「花生」。
    /// 反向匹配要求食材名至少 2 个字，避免「盐」这类单字造成大面积误杀。
    /// </remarks>
    public static bool ContainsAvoided(DishCandidate dish, IReadOnlySet<string> expandedAvoid)
    {
        foreach (var ingredient in dish.Ingredients)
        {
            var name = ingredient.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            foreach (var avoid in expandedAvoid)
            {
                if (name.Contains(avoid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (name.Length >= 2 && avoid.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 常见过敏原同义词表。随菜品种子数据一起维护。
    /// </summary>
    /// <remarks>
    /// 用户写的上位词（如「坚果」）在这里展开为具体食材名，否则匹配不到。
    /// </remarks>
    private static readonly Dictionary<string, string[]> AllergenSynonyms =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [CommonAllergens.TreeNut] =
                ["花生", "腰果", "核桃", "杏仁", "松子", "开心果", "榛子", "夏威夷果", "花生米"],
            [CommonAllergens.Seafood] =
                ["虾", "蟹", "贝", "蛤", "牡蛎", "扇贝", "鱿鱼", "章鱼", "龙虾", "鱼露", "蚝油"],
            [CommonAllergens.Dairy] =
                ["牛奶", "奶油", "黄油", "奶酪", "芝士", "炼乳", "淡奶油"],
            [CommonAllergens.Gluten] =
                ["面粉", "面条", "面包", "馒头", "饺子皮", "小麦", "河粉", "年糕"],
            [CommonAllergens.Egg] =
                ["鸡蛋", "鸭蛋", "蛋清", "蛋黄", "皮蛋", "咸蛋", "蛋液"],
            [CommonAllergens.Soy] =
                ["黄豆", "豆腐", "豆浆", "豆干", "腐竹", "酱油", "豆瓣酱", "生抽", "老抽", "味极鲜"],
            [CommonAllergens.Peanut] =
                ["花生", "花生米", "花生酱", "花生碎"],
        };
}
