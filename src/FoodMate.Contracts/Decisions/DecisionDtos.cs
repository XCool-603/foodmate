using FoodMate.Contracts.Dishes;

namespace FoodMate.Contracts.Decisions;

/// <summary>决策请求。</summary>
public sealed record DecisionSuggestRequest
{
    /// <summary>餐次；null 时由服务端按时间推断。</summary>
    public short? MealType { get; init; }

    /// <summary>就餐方式：0 随便 / 1 外卖 / 2 堂食 / 3 自己做。</summary>
    public short DiningMode { get; init; }

    /// <summary>用餐人数。</summary>
    public short PartySize { get; init; } = 1;

    /// <summary>本次预算下限（分）；null 时用画像值。</summary>
    public int? BudgetMinCents { get; init; }

    /// <summary>本次预算上限（分）；null 时用画像值。</summary>
    public int? BudgetMaxCents { get; init; }

    /// <summary>当前位置。</summary>
    public GeoPointDto? Location { get; init; }

    /// <summary>天气：clear / rain / hot / cold / snow。</summary>
    public string? Weather { get; init; }

    /// <summary>快捷需求标签，最多 3 个。</summary>
    public IReadOnlyList<string> MoodTags { get; init; } = [];

    /// <summary>要排除的菜品 ID（「换一批」时传入上一批）。</summary>
    public IReadOnlyList<Guid> ExcludeDishIds { get; init; } = [];

    /// <summary>分数抖动标准差（分）。0 = 确定性；「换一批」建议传 3。</summary>
    public double ExploreJitter { get; init; }
}

/// <summary>经纬度。</summary>
/// <param name="Latitude">纬度。</param>
/// <param name="Longitude">经度。</param>
public sealed record GeoPointDto(double Latitude, double Longitude);

/// <summary>决策响应。</summary>
public sealed record DecisionSuggestResponse
{
    /// <summary>决策会话 ID，用于回传用户选择。</summary>
    public Guid SessionId { get; init; }

    /// <summary>引擎版本。</summary>
    public string EngineVersion { get; init; } = string.Empty;

    /// <summary>引擎耗时（毫秒）。</summary>
    public long ElapsedMs { get; init; }

    /// <summary>硬过滤后的候选总数。</summary>
    public int CandidateCount { get; init; }

    /// <summary>被硬过滤掉的数量。</summary>
    public int FilteredOutCount { get; init; }

    /// <summary>降级层级：0 未降级；&gt;0 表示放宽了条件。</summary>
    public int RelaxedLevel { get; init; }

    /// <summary>转盘候选的菜品 ID（默认 Top 8），与 <see cref="Ranked"/> 前 N 项同序。</summary>
    public IReadOnlyList<Guid> WheelPicks { get; init; } = [];

    /// <summary>按分数降序的推荐列表。</summary>
    public IReadOnlyList<ScoredDishDto> Ranked { get; init; } = [];
}

/// <summary>单条推荐。</summary>
public sealed record ScoredDishDto
{
    /// <summary>排名，从 1 开始。</summary>
    public int Rank { get; init; }

    /// <summary>菜品 ID。</summary>
    public Guid DishId { get; init; }

    /// <summary>菜名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>总分 0–100。</summary>
    public double Score { get; init; }

    /// <summary>各维度归一化分（0–1），键为 camelCase 维度名。</summary>
    public IReadOnlyDictionary<string, double> Breakdown { get; init; }
        = new Dictionary<string, double>();

    /// <summary>已应用的乘性惩罚。</summary>
    public IReadOnlyList<PenaltyDto> Penalties { get; init; } = [];

    /// <summary>人话推荐理由，1–2 条。</summary>
    public IReadOnlyList<string> Reasons { get; init; } = [];

    /// <summary>菜品详情。</summary>
    public DishBriefDto Dish { get; init; } = new();
}

/// <summary>已应用的惩罚。</summary>
/// <param name="Code">惩罚代号。</param>
/// <param name="Multiplier">乘数。</param>
/// <param name="Description">可读说明。</param>
public sealed record PenaltyDto(string Code, double Multiplier, string Description);

/// <summary>回传用户选择的请求。</summary>
public sealed record DecisionChooseRequest
{
    /// <summary>用户最终选择的菜品 ID。</summary>
    public Guid DishId { get; init; }

    /// <summary>来源：wheel（转盘）/ list（榜单）/ other。</summary>
    public string Source { get; init; } = "list";
}

/// <summary>回传用户选择的响应。</summary>
public sealed record DecisionChooseResponse
{
    /// <summary>决策会话 ID。</summary>
    public Guid SessionId { get; init; }

    /// <summary>选中的菜品 ID。</summary>
    public Guid DishId { get; init; }

    /// <summary>该菜品在推荐中的排名；未出现在候选中时为 0。</summary>
    public int Rank { get; init; }

    /// <summary>选择时间。</summary>
    public DateTimeOffset ChosenAt { get; init; }
}

/// <summary>引擎元信息，供前端把 breakdown 换算成贡献度条形图。</summary>
public sealed record EngineMetaDto
{
    /// <summary>引擎版本。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>各维度权重，键为 camelCase 维度名。</summary>
    public IReadOnlyDictionary<string, double> Weights { get; init; }
        = new Dictionary<string, double>();

    /// <summary>各维度的中文显示名。</summary>
    public IReadOnlyDictionary<string, string> DimensionLabels { get; init; }
        = new Dictionary<string, string>();
}
