namespace FoodMate.Core.Decision;

/// <summary>
/// 七维打分权重。总和必须为 <c>1.0</c>。
/// </summary>
/// <remarks>
/// <para>
/// 默认值体现产品调性：<b>新鲜度(0.20) 刻意高于偏好强度(0.15)</b>。
/// 因为这是「决策助手」而非「猜你喜欢」——若偏好强度更高，
/// 产品会退化成永远推荐同一道菜，用户很快会腻。
/// </para>
/// <para>
/// 这个比例是需要用真实数据调优的<b>首要参数</b>。
/// </para>
/// </remarks>
public sealed class ScoreWeights
{
    /// <summary>口味匹配权重。</summary>
    public double Taste { get; set; } = 0.25;

    /// <summary>新鲜度权重。</summary>
    public double Freshness { get; set; } = 0.20;

    /// <summary>偏好强度权重。</summary>
    public double Affinity { get; set; } = 0.15;

    /// <summary>时段契合权重。</summary>
    public double TimeSlot { get; set; } = 0.15;

    /// <summary>预算匹配权重。</summary>
    public double Budget { get; set; } = 0.10;

    /// <summary>场景契合权重。</summary>
    public double Context { get; set; } = 0.10;

    /// <summary>探索度权重。</summary>
    public double Exploration { get; set; } = 0.05;

    /// <summary>按维度取权重。</summary>
    public double this[ScoreDimension dimension] => dimension switch
    {
        ScoreDimension.Taste => Taste,
        ScoreDimension.Freshness => Freshness,
        ScoreDimension.Affinity => Affinity,
        ScoreDimension.TimeSlot => TimeSlot,
        ScoreDimension.Budget => Budget,
        ScoreDimension.Context => Context,
        ScoreDimension.Exploration => Exploration,
        _ => 0,
    };

    /// <summary>权重总和。</summary>
    public double Sum =>
        Taste + Freshness + Affinity + TimeSlot + Budget + Context + Exploration;

    /// <summary>按维度写入权重。</summary>
    public void Set(ScoreDimension dimension, double value)
    {
        switch (dimension)
        {
            case ScoreDimension.Taste: Taste = value; break;
            case ScoreDimension.Freshness: Freshness = value; break;
            case ScoreDimension.Affinity: Affinity = value; break;
            case ScoreDimension.TimeSlot: TimeSlot = value; break;
            case ScoreDimension.Budget: Budget = value; break;
            case ScoreDimension.Context: Context = value; break;
            case ScoreDimension.Exploration: Exploration = value; break;
        }
    }
}
