namespace FoodMate.Contracts.Profile;

/// <summary>就餐方式偏好权重。</summary>
public sealed record DiningModeWeightsDto
{
    /// <summary>外卖权重。</summary>
    public double Takeout { get; init; } = 1.0 / 3.0;

    /// <summary>堂食权重。</summary>
    public double DineIn { get; init; } = 1.0 / 3.0;

    /// <summary>自己做权重。</summary>
    public double Homemade { get; init; } = 1.0 / 3.0;
}

/// <summary>口味画像。</summary>
public sealed record PreferenceDto
{
    /// <summary>辣度偏好 0–5。</summary>
    public short SpicyLevel { get; init; }

    /// <summary>每餐预算下限（分）。</summary>
    public int BudgetMinCents { get; init; }

    /// <summary>每餐预算上限（分）。</summary>
    public int BudgetMaxCents { get; init; }

    /// <summary>忌口 / 过敏食材。<b>硬过滤条件</b>。</summary>
    public IReadOnlyList<string> AvoidIngredients { get; init; } = [];

    /// <summary>偏好菜系。</summary>
    public IReadOnlyList<short> PreferredCuisines { get; init; } = [];

    /// <summary>就餐方式偏好权重。</summary>
    public DiningModeWeightsDto DiningModeWeights { get; init; } = new();

    /// <summary>可接受距离（米）。</summary>
    public int MaxDistanceMeters { get; init; }

    /// <summary>是否已完成新用户偏好引导。</summary>
    public bool OnboardingCompleted { get; init; }

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>更新口味画像（字段可选，只传要改的）。</summary>
public sealed record UpdatePreferenceRequest
{
    /// <summary>辣度偏好 0–5。</summary>
    public short? SpicyLevel { get; init; }

    /// <summary>预算下限（分）。</summary>
    public int? BudgetMinCents { get; init; }

    /// <summary>预算上限（分）。</summary>
    public int? BudgetMaxCents { get; init; }

    /// <summary>忌口食材（全量覆盖）。</summary>
    public IReadOnlyList<string>? AvoidIngredients { get; init; }

    /// <summary>偏好菜系（全量覆盖）。</summary>
    public IReadOnlyList<short>? PreferredCuisines { get; init; }

    /// <summary>可接受距离（米）。</summary>
    public int? MaxDistanceMeters { get; init; }
}

/// <summary>新用户偏好引导提交。</summary>
public sealed record OnboardingRequest
{
    /// <summary>辣度偏好 0–5。</summary>
    public short SpicyLevel { get; init; } = 2;

    /// <summary>预算下限（分）。</summary>
    public int BudgetMinCents { get; init; } = 1500;

    /// <summary>预算上限（分）。</summary>
    public int BudgetMaxCents { get; init; } = 5000;

    /// <summary>忌口食材。</summary>
    public IReadOnlyList<string> AvoidIngredients { get; init; } = [];

    /// <summary>偏好菜系。</summary>
    public IReadOnlyList<short> PreferredCuisines { get; init; } = [];

    /// <summary>主要就餐方式。</summary>
    public short DiningMode { get; init; }
}

/// <summary>根据历史记录学到的画像建议。</summary>
/// <remarks>
/// <b>只建议，不自动应用。</b>用户在手填画像里表达的是意图，历史记录反映的是行为，
/// 两者不一致时应由用户决定，而不是静默覆盖。
/// </remarks>
public sealed record PreferenceSuggestionDto
{
    /// <summary>建议的辣度偏好。</summary>
    public short? SpicyLevel { get; init; }

    /// <summary>建议的预算下限（分）。</summary>
    public int? BudgetMinCents { get; init; }

    /// <summary>建议的预算上限（分）。</summary>
    public int? BudgetMaxCents { get; init; }

    /// <summary>建议的偏好菜系。</summary>
    public IReadOnlyList<short>? PreferredCuisines { get; init; }

    /// <summary>建议的偏好菜系名称。</summary>
    public IReadOnlyList<string>? PreferredCuisineLabels { get; init; }

    /// <summary>参与学习的样本量。</summary>
    public int SampleSize { get; init; }

    /// <summary>是否给出了任何建议。</summary>
    public bool HasAny { get; init; }

    /// <summary>学习所依据的记录条数下限（样本不足时为 0）。</summary>
    public int RequiredSampleSize { get; init; }
}
