namespace FoodMate.Core.Decision;

/// <summary>决策打分的七个维度。</summary>
public enum ScoreDimension
{
    /// <summary>口味匹配：辣度、菜系、快捷需求。</summary>
    Taste,

    /// <summary>新鲜度：避免重复吃同一道菜或同一菜系。</summary>
    Freshness,

    /// <summary>偏好强度：基于历史评分与食用频次。</summary>
    Affinity,

    /// <summary>时段契合：早/午/晚/夜宵 + 季节。</summary>
    TimeSlot,

    /// <summary>预算匹配。</summary>
    Budget,

    /// <summary>场景契合：就餐方式、距离、天气、人数。</summary>
    Context,

    /// <summary>探索度：给没吃过的菜一点机会。</summary>
    Exploration,
}
