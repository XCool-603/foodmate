using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision;

/// <summary>
/// 决策引擎的全部可调参数。
/// </summary>
/// <remarks>
/// 全部外置到配置（<c>appsettings.json → DecisionEngine</c>），
/// <b>不发版即可调优</b>。启动时调用 <see cref="Validate"/> 快速失败。
/// </remarks>
public sealed class EngineOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "DecisionEngine";

    /// <summary>引擎版本，写入 <c>decision_sessions</c> 便于 A/B 与回溯。</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>七维权重。</summary>
    public ScoreWeights Weights { get; set; } = new();

    /// <summary>新鲜度参数。</summary>
    public FreshnessOptions Freshness { get; set; } = new();

    /// <summary>偏好强度参数。</summary>
    public AffinityOptions Affinity { get; set; } = new();

    /// <summary>乘性惩罚参数。</summary>
    public PenaltyOptions Penalties { get; set; } = new();

    /// <summary>冷启动参数。</summary>
    public ColdStartOptions ColdStart { get; set; } = new();

    /// <summary>返回条数。</summary>
    public TopNOptions TopN { get; set; } = new();

    /// <summary>分数抖动。</summary>
    public JitterOptions Jitter { get; set; } = new();

    /// <summary>校验参数合法性；不合法时抛 <see cref="InvalidOperationException"/>。</summary>
    public void Validate()
    {
        if (Math.Abs(Weights.Sum - 1.0) > 1e-6)
        {
            throw new InvalidOperationException(
                $"决策引擎权重之和必须为 1.0，当前为 {Weights.Sum:F6}。");
        }

        if (Weights.Taste < 0 || Weights.Freshness < 0 || Weights.Affinity < 0
            || Weights.TimeSlot < 0 || Weights.Budget < 0 || Weights.Context < 0
            || Weights.Exploration < 0)
        {
            throw new InvalidOperationException("决策引擎权重不能为负数。");
        }

        if (Freshness.DishTauDays <= 0 || Freshness.CuisineTauDays <= 0)
        {
            throw new InvalidOperationException("新鲜度衰减常数 τ 必须大于 0。");
        }

        if (Math.Abs(Freshness.DishWeight + Freshness.CuisineWeight - 1.0) > 1e-6)
        {
            throw new InvalidOperationException("菜品级与菜系级新鲜度权重之和必须为 1.0。");
        }

        if (TopN.Wheel <= 0 || TopN.ResultList <= 0 || TopN.DisplayList <= 0)
        {
            throw new InvalidOperationException("TopN 配置必须为正数。");
        }

        if (TopN.DisplayList > TopN.ResultList || TopN.Wheel > TopN.ResultList)
        {
            throw new InvalidOperationException("DisplayList 与 Wheel 不能大于 ResultList。");
        }
    }
}

/// <summary>新鲜度（<c>S_fresh</c>）参数。</summary>
public sealed class FreshnessOptions
{
    /// <summary>菜品级时间衰减常数（天）。</summary>
    public double DishTauDays { get; set; } = 7.0;

    /// <summary>菜系级时间衰减常数（天）。粒度更粗故更宽松。</summary>
    public double CuisineTauDays { get; set; } = 10.0;

    /// <summary>菜品级权重。</summary>
    public double DishWeight { get; set; } = 0.7;

    /// <summary>菜系级权重。</summary>
    public double CuisineWeight { get; set; } = 0.3;

    /// <summary>时间衰减项的指数（几何平均用）。</summary>
    public double RecencyPower { get; set; } = 0.6;

    /// <summary>频次项的指数（几何平均用）。</summary>
    public double FrequencyPower { get; set; } = 0.4;

    /// <summary>频次惩罚系数：<c>1 / (1 + α · 近30天次数)</c>。</summary>
    public double FrequencyAlpha { get; set; } = 0.3;
}

/// <summary>偏好强度（<c>S_affinity</c>）参数。</summary>
public sealed class AffinityOptions
{
    /// <summary>频次封顶次数（达到即满分）。</summary>
    public int FreqCap { get; set; } = 10;

    /// <summary>无评分时的中性分。</summary>
    public double DefaultRatingScore { get; set; } = 0.5;

    /// <summary>没吃过时的频次分。</summary>
    public double DefaultFreqScore { get; set; } = 0.3;

    /// <summary>没吃过时的「还想再吃」率（中性值）。</summary>
    public double DefaultAgainRate { get; set; } = 0.5;

    /// <summary>评分项权重。</summary>
    public double RatingWeight { get; set; } = 0.5;

    /// <summary>频次项权重。</summary>
    public double FrequencyWeight { get; set; } = 0.3;

    /// <summary>「还想再吃」项权重。</summary>
    public double AgainWeight { get; set; } = 0.2;
}

/// <summary>乘性惩罚参数。</summary>
public sealed class PenaltyOptions
{
    /// <summary>24 小时内吃过同一道菜的惩罚。</summary>
    public double EatenWithin24h { get; set; } = 0.10;

    /// <summary>差评惩罚。</summary>
    public double Disliked { get; set; } = 0.25;

    /// <summary>差评阈值：平均评分 ≤ 此值触发。</summary>
    public double DislikedThreshold { get; set; } = 2.0;

    /// <summary>「一般般」惩罚。</summary>
    public double Meh { get; set; } = 0.70;

    /// <summary>「一般般」阈值：平均评分 ≤ 此值触发。</summary>
    public double MehThreshold { get; set; } = 3.0;

    /// <summary>明确标记「不想再吃」的惩罚。</summary>
    public double NeverAgain { get; set; } = 0.30;

    /// <summary>24 小时惩罚的时间窗口。</summary>
    public TimeSpan RecentWindow { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>冷启动参数。</summary>
public sealed class ColdStartOptions
{
    /// <summary>用户记录数低于此值时，用全局热度兜底偏好强度。</summary>
    public int MinRecordsForAffinity { get; set; } = 3;

    /// <summary>兜底时全局热度的混合比例。</summary>
    public double PopularityBlend { get; set; } = 0.5;
}

/// <summary>返回条数配置。</summary>
public sealed class TopNOptions
{
    /// <summary>转盘候选数。</summary>
    public int Wheel { get; set; } = 8;

    /// <summary>结果列表返回数。</summary>
    public int ResultList { get; set; } = 20;

    /// <summary>榜单默认展示数。</summary>
    public int DisplayList { get; set; } = 5;
}

/// <summary>分数抖动配置。</summary>
public sealed class JitterOptions
{
    /// <summary>默认抖动标准差（分）。0 = 完全确定性。</summary>
    public double DefaultSigma { get; set; }

    /// <summary>「换一批」时的抖动标准差（分）。</summary>
    public double RefreshSigma { get; set; } = 3.0;
}

/// <summary>
/// 菜品分类的默认价格（分），用于菜品没有价格信息时兜底。
/// </summary>
public static class CategoryDefaultPrice
{
    /// <summary>取指定分类的默认价格。</summary>
    public static int For(DishCategory category) => category switch
    {
        DishCategory.Staple => 800,
        DishCategory.Meat => 3800,
        DishCategory.Vegetable => 1800,
        DishCategory.Soup => 2200,
        DishCategory.Snack => 1200,
        DishCategory.Dessert => 1500,
        DishCategory.Drink => 1000,
        DishCategory.Breakfast => 800,
        DishCategory.Hotpot => 8000,
        _ => 2000,
    };
}
