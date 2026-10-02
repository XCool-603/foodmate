using FoodMate.Core.Decision;

namespace FoodMate.Contracts.Decisions;

/// <summary>
/// <see cref="ScoreDimension"/> 与前端约定的字符串键之间的映射。
/// </summary>
/// <remarks>
/// 用显式映射而非 <c>ToString()</c>，避免将来重命名枚举成员时静默改变 API 契约。
/// </remarks>
public static class ScoreDimensionKeys
{
    private static readonly Dictionary<ScoreDimension, string> ToKeyMap = new()
    {
        [ScoreDimension.Taste] = "taste",
        [ScoreDimension.Freshness] = "freshness",
        [ScoreDimension.Affinity] = "affinity",
        [ScoreDimension.TimeSlot] = "timeSlot",
        [ScoreDimension.Budget] = "budget",
        [ScoreDimension.Context] = "context",
        [ScoreDimension.Exploration] = "exploration",
    };

    private static readonly Dictionary<ScoreDimension, string> ToLabelMap = new()
    {
        [ScoreDimension.Taste] = "口味匹配",
        [ScoreDimension.Freshness] = "新鲜度",
        [ScoreDimension.Affinity] = "你的偏好",
        [ScoreDimension.TimeSlot] = "时段合适",
        [ScoreDimension.Budget] = "预算匹配",
        [ScoreDimension.Context] = "场景契合",
        [ScoreDimension.Exploration] = "没吃过",
    };

    /// <summary>取维度对应的 API 键名。</summary>
    public static string Key(ScoreDimension dimension)
        => ToKeyMap.TryGetValue(dimension, out var key) ? key : dimension.ToString().ToLowerInvariant();

    /// <summary>取维度的中文显示名。</summary>
    public static string Label(ScoreDimension dimension)
        => ToLabelMap.TryGetValue(dimension, out var label) ? label : Key(dimension);
}
