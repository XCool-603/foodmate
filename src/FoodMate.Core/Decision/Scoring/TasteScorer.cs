using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 口味匹配（<c>S_taste</c>，默认权重 0.25）。
/// </summary>
/// <remarks>
/// <code>
/// MoodTags 为空：S_taste = 0.55·S_spicy + 0.45·S_cuisine
/// 否则：        S_taste = 0.45·S_spicy + 0.30·S_cuisine + 0.25·S_mood
/// </code>
/// </remarks>
public sealed class TasteScorer : IScorer
{
    /// <inheritdoc />
    public ScoreDimension Dimension => ScoreDimension.Taste;

    /// <inheritdoc />
    public double Score(DishCandidate dish, DecisionContext context)
    {
        var spicy = ScoreSpicy(dish.SpicyLevel, context.Preference.SpicyLevel);
        var cuisine = ScoreCuisine(dish.Cuisine, context.Preference.PreferredCuisines);
        var moods = context.Request.MoodTags;

        // 没有快捷需求时不引入 S_mood，把权重按比例分给辣度与菜系
        if (moods.Count == 0)
        {
            return 0.55 * spicy + 0.45 * cuisine;
        }

        return 0.45 * spicy + 0.30 * cuisine + 0.25 * ScoreMood(dish, moods);
    }

    /// <summary>
    /// 辣度匹配。惩罚是<b>非对称</b>的：比偏好更辣罚得重，比偏好更淡罚得轻。
    /// </summary>
    /// <remarks>
    /// <code>
    /// Δ_over  = max(0, dish - pref)   // 比偏好更辣
    /// Δ_under = max(0, pref - dish)   // 比偏好更淡
    /// S = 1 - 0.05·Δ_under - 0.15·Δ_over
    /// </code>
    /// 另加<b>低辣度敏感修正</b>（安全护栏）：完全不吃辣的用户不该在转盘上抽到麻辣香锅。
    /// </remarks>
    public static double ScoreSpicy(short dishSpicy, short prefSpicy)
    {
        // 安全护栏：低辣度偏好遇到高辣度菜品
        if (prefSpicy == 0 && dishSpicy >= 3)
        {
            return 0.10;
        }

        if (prefSpicy == 1 && dishSpicy >= 4)
        {
            return 0.20;
        }

        var over = Math.Max(0, dishSpicy - prefSpicy);
        var under = Math.Max(0, prefSpicy - dishSpicy);

        return 1.0 - 0.05 * under - 0.15 * over;
    }

    /// <summary>
    /// 菜系匹配。用户没填偏好菜系时返回中性 0.5，<b>不惩罚任何菜系</b>。
    /// </summary>
    public static double ScoreCuisine(Cuisine dishCuisine, IReadOnlyList<Cuisine> preferred)
    {
        if (preferred.Count == 0)
        {
            return 0.5;
        }

        return preferred.Contains(dishCuisine) ? 1.0 : 0.4;
    }

    /// <summary>
    /// 快捷需求匹配。未注册的标签会从分母中剔除，<b>不惩罚未知标签</b>。
    /// </summary>
    public static double ScoreMood(DishCandidate dish, IReadOnlyList<string> moodTags)
    {
        var known = moodTags.Where(MoodMatchers.ContainsKey).ToList();

        if (known.Count == 0)
        {
            return 0.5;
        }

        var hit = known.Count(tag => MoodMatchers[tag](dish));
        return (double)hit / known.Count;
    }

    /// <summary>快捷标签到菜品属性的映射。必须与 <see cref="MoodTags"/> 保持一一对应。</summary>
    private static readonly Dictionary<string, Func<DishCandidate, bool>> MoodMatchers =
        new(StringComparer.Ordinal)
        {
            [MoodTags.Spicy] = d => d.SpicyLevel >= 3,
            [MoodTags.Light] = d => d.SpicyLevel <= 1 && !d.Tags.Contains("重口"),
            [MoodTags.Warming] = d => d.Tags.Contains("暖胃") || d.Category == DishCategory.Soup,
            [MoodTags.Quick] = d => d.CookMinutes is > 0 and <= 20 || d.Tags.Contains("快手"),
            [MoodTags.RiceFriendly] = d => d.Tags.Contains("下饭"),
            [MoodTags.Soupy] = d => d.Category == DishCategory.Soup,
            [MoodTags.Refreshing] = d => d.Tags.Contains("清淡") || d.Category == DishCategory.Vegetable,
        };
}
