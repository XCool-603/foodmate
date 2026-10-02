# 美食伴侣 · 决策引擎设计

> 配套文档：[总设计文档](./DESIGN.md) · [数据库设计](./DATABASE.md) · [API 接口契约](./API.md)

| 项目 | 内容 |
|---|---|
| 文档版本 | v1.0 |
| 引擎版本 | `1.0.0` |
| 位置 | `FoodMate.Core/Decision/` |
| 依赖 | **零外部依赖**（不注入 DbContext、不调网络、不用 `DateTime.Now`） |

---

## 1. 设计目标

| # | 目标 | 说明 |
|---|---|---|
| G1 | **可解释** | 每个结果必须能说出「为什么推荐它」。这是本产品相对竞品的核心差异 |
| G2 | **可测试** | 纯函数式，输入 → 输出，无副作用。相同输入必须产生相同输出 |
| G3 | **冷启动可用** | 零画像、零历史的新用户也必须能给出合理结果 |
| G4 | **防重复** | 不能永远推荐那三道菜，但也不能推荐用户讨厌的 |
| G5 | **可调参** | 权重与阈值全部外置到配置，不发版即可调优 |
| G6 | **快** | 2000 候选 × 7 维度 < 10ms（内存计算，无 IO） |

### 非目标

- ❌ 不做机器学习（v1）。先用规则跑通，用真实数据验证后再上模型。
- ❌ 不做实时协同过滤（没有用户量）。
- ❌ 不做「餐厅推荐」（v1 无商家维度）。

---

## 2. 输入 / 输出契约

### 2.1 输入

```csharp
namespace FoodMate.Core.Decision;

/// <summary>决策请求：用户本次的显式意图</summary>
public sealed record DecisionRequest
{
    public required Guid UserId { get; init; }

    /// <summary>餐次；null 时由 Now 推断</summary>
    public MealType? MealType { get; init; }

    /// <summary>就餐方式；Whatever 表示不限</summary>
    public DiningMode DiningMode { get; init; } = DiningMode.Whatever;

    public short PartySize { get; init; } = 1;

    /// <summary>本次预算覆盖（分）；null 时用画像值</summary>
    public int? BudgetMinCents { get; init; }
    public int? BudgetMaxCents { get; init; }

    public GeoPoint? Location { get; init; }

    /// <summary>天气：clear / rain / hot / cold / snow</summary>
    public string? Weather { get; init; }

    /// <summary>快捷需求标签：辣的 / 清淡的 / 暖胃的 / 快手 / 下饭</summary>
    public IReadOnlyList<string> MoodTags { get; init; } = [];

    /// <summary>要排除的菜品（「换一批」时传入上一批）</summary>
    public IReadOnlyList<Guid> ExcludeDishIds { get; init; } = [];

    /// <summary>分数抖动标准差（分）；0 = 完全确定性。仅「换一批」时用</summary>
    public double ExploreJitter { get; init; } = 0;

    /// <summary>当前时间（UTC）。由调用方注入，保证可测试</summary>
    public required DateTimeOffset Now { get; init; }
}

public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>决策引擎的完整输入上下文（由 Application 层组装）</summary>
public sealed record DecisionContext(
    DecisionRequest Request,
    UserPreferenceSnapshot Preference,
    IReadOnlyList<DishCandidate> Candidates,
    IReadOnlyDictionary<Guid, DishStatSnapshot> DishStats,
    IReadOnlyDictionary<Cuisine, CuisineStatSnapshot> CuisineStats,
    EngineOptions Options,
    /// <summary>用户历史记录总数，用于冷启动判断</summary>
    int UserRecordCount);

/// <summary>参与打分的菜品（已展开 JSON 字段）</summary>
public sealed record DishCandidate(
    Guid Id,
    string Name,
    Cuisine Cuisine,
    DishCategory Category,
    short SpicyLevel,
    int? PriceMinCents,
    int? PriceMaxCents,
    int? Calories,
    IReadOnlyList<Ingredient> Ingredients,
    IReadOnlyList<string> Tags,
    MealTimeMask MealTimes,
    SeasonMask Seasons,
    bool HasRecipe,
    short? CookMinutes,
    int Popularity,
    /// <summary>堂食/外卖模式下的距离（km）；未知为 null</summary>
    double? DistanceKm);

public sealed record Ingredient(string Name, string? Amount, bool IsCommonAllergen);

/// <summary>用户画像快照</summary>
public sealed record UserPreferenceSnapshot(
    short SpicyLevel,
    int BudgetMinCents,
    int BudgetMaxCents,
    IReadOnlyList<string> AvoidIngredients,
    IReadOnlyList<Cuisine> PreferredCuisines,
    IReadOnlyDictionary<DiningMode, double> DiningModeWeights,
    int MaxDistanceM)
{
    public static UserPreferenceSnapshot Default { get; } = new(
        SpicyLevel: 2,
        BudgetMinCents: 1500,
        BudgetMaxCents: 5000,
        AvoidIngredients: [],
        PreferredCuisines: [],
        DiningModeWeights: new Dictionary<DiningMode, double>(),
        MaxDistanceM: 2000);
}

/// <summary>用户×菜品历史</summary>
public sealed record DishStatSnapshot(
    int EatCount,
    DateTimeOffset? LastEatenAt,
    int RatingSum,
    int RatingCount,
    int WouldEatAgainCount)
{
    public double? AverageRating => RatingCount > 0 ? (double)RatingSum / RatingCount : null;
}

/// <summary>用户×菜系历史</summary>
public sealed record CuisineStatSnapshot(int EatCountTotal, DateTimeOffset? LastEatenAt);
```

### 2.2 输出

```csharp
public sealed record DecisionResult(
    IReadOnlyList<ScoredDish> Ranked,
    /// <summary>硬过滤后的候选总数（诊断用）</summary>
    int CandidateCount,
    /// <summary>被硬过滤掉的数量（诊断用）</summary>
    int FilteredOutCount,
    string EngineVersion,
    long ElapsedMs);

public sealed record ScoredDish(
    Guid DishId,
    string Name,
    /// <summary>0–100</summary>
    double Score,
    IReadOnlyDictionary<ScoreDimension, double> Breakdown,
    IReadOnlyList<AppliedPenalty> Penalties,
    IReadOnlyList<string> Reasons);

public enum ScoreDimension
{
    Taste, Freshness, Affinity, TimeSlot, Budget, Context, Exploration
}

public sealed record AppliedPenalty(string Code, double Multiplier, string Description);
```

> 💡 `Breakdown` 里存的是**归一化后的维度分**（0–1），不是加权后的贡献。加权贡献由 `Score` 与 `Breakdown` 反推：`contribution = weight × breakdown`。这样便于前端展示雷达图/条形图。

---

## 3. 整体流程

```
┌──────────────────────────────────────────────────────────────┐
│ 阶段 1：硬过滤（Hard Filter）                                 │
│   候选菜品 → 排除下架/删除/他人私有/含忌口/含过敏原/不满足就餐方式 │
│   输出：filteredCandidates                                    │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ 阶段 2：保底检查                                              │
│   若 filteredCandidates.Count < WheelSize(8)                  │
│     → 逐步放宽：先放宽时段限制，再放宽距离，绝不放开忌口        │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ 阶段 3：七维打分                                              │
│   对每个候选计算 S_taste / S_fresh / S_affinity / S_timeSlot   │
│                / S_budget / S_context / S_explore             │
│   TotalScore = Σ(wᵢ · Sᵢ)                                     │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ 阶段 4：乘性惩罚                                              │
│   TotalScore ×= Π Pⱼ   （24h内吃过 / 差评 / 明确不想再吃）      │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ 阶段 5：排除 + 抖动 + 排序                                    │
│   移除 ExcludeDishIds → 加 N(0, σ) 抖动 → 降序排序             │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
┌──────────────────────────────────────────────────────────────┐
│ 阶段 6：生成推荐理由                                          │
│   取贡献最大的 2 个维度 → 套用文案模板                          │
└───────────────────────────┬──────────────────────────────────┘
                            ▼
                     Top 20 → 返回
                     Top 8  → 转盘
                     Top 5  → 榜单默认展示
```

---

## 4. 阶段 1：硬过滤规则

> ⚠️ **硬过滤是不可协商的**。尤其是忌口与过敏原——这是安全问题，绝不能靠"分数低"来处理。

| # | 规则 | 实现 |
|---|---|---|
| F1 | 排除下架/已删除 | `dish.IsActive && !dish.IsDeleted` |
| F2 | 排除他人私有菜品 | `dish.IsBuiltin \|\| dish.OwnerUserId == userId` |
| F3 | **排除含忌口食材** | 见下方匹配算法 |
| F4 | **排除含过敏原** | `ingredient.IsCommonAllergen && 命中忌口` |
| F5 | 自己做模式必须有菜谱 | `DiningMode == Homemade → dish.HasRecipe` |
| F6 | 排除用户显式排除的菜 | `!ExcludeDishIds.Contains(dish.Id)` |

### 4.1 忌口匹配算法

用户忌口是自由文本（「花生」「香菜」「海鲜」），菜品食材也是文本。需要**双向包含匹配**：

```csharp
public static bool ContainsAvoidedIngredient(
    DishCandidate dish,
    IReadOnlyList<string> avoidList)
{
    if (avoidList.Count == 0) return false;

    foreach (var ing in dish.Ingredients)
    {
        foreach (var avoid in avoidList)
        {
            // 双向包含：用户写"花生" 能匹配 "花生米"；用户写"花生米" 也能匹配 "花生"
            if (ing.Name.Contains(avoid, StringComparison.OrdinalIgnoreCase)
             || avoid.Contains(ing.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
    }
    return false;
}
```

**已知局限与对策**

| 局限 | 对策 |
|---|---|
| 用户写「坚果」，菜品写「腰果」 | 维护**同义词表**：坚果 → [花生, 腰果, 核桃, 杏仁, 松子, 开心果, 榛子] |
| 菜品食材字段不全 | 种子里子数据**必须**完整填写常见过敏原；宁可多填不可漏填 |
| 复合菜品（如「宫保鸡丁」含花生但食材只写了"鸡丁"） | 菜品库审核时标注「含常见过敏原」布尔标记，作为兜底 |

```csharp
/// <summary>常见过敏原同义词表，随种子数据一起维护</summary>
private static readonly Dictionary<string, string[]> AllergenSynonyms = new()
{
    ["坚果"] = ["花生", "腰果", "核桃", "杏仁", "松子", "开心果", "榛子", "夏威夷果"],
    ["海鲜"] = ["虾", "蟹", "贝", "蛤", "牡蛎", "扇贝", "鱿鱼", "章鱼", "龙虾"],
    ["乳制品"] = ["牛奶", "奶油", "黄油", "奶酪", "芝士", "炼乳"],
    ["麸质"] = ["面粉", "面条", "面包", "馒头", "饺子皮", "小麦"],
    ["蛋"] = ["鸡蛋", "鸭蛋", "蛋清", "蛋黄", "皮蛋", "咸蛋"],
    ["大豆"] = ["黄豆", "豆腐", "豆浆", "豆干", "腐竹", "酱油", "豆瓣酱"],
};
```

> 💡 匹配时**同时展开同义词**：用户写「坚果」→ 展开为 8 个词 → 逐个匹配菜品食材。

---

## 5. 阶段 3：七维打分

### 5.0 总公式

```
TotalScore = Σᵢ (wᵢ · Sᵢ)        其中 Σwᵢ = 1，Sᵢ ∈ [0, 1]
FinalScore = TotalScore × Πⱼ Pⱼ   Pⱼ ∈ (0, 1]
Score100   = FinalScore × 100
```

**默认权重**

| 维度 | 符号 | 权重 | 设计意图 |
|---|---|---|---|
| 口味匹配 | `S_taste` | **0.25** | 最基础：得对胃口 |
| 新鲜度 | `S_fresh` | **0.20** | 防重复：别老吃那几样 |
| 偏好强度 | `S_affinity` | **0.15** | 尊重历史：爱吃的要照顾 |
| 时段契合 | `S_timeSlot` | **0.15** | 常识：早上不推红烧肉 |
| 预算匹配 | `S_budget` | **0.10** | 现实约束 |
| 场景契合 | `S_context` | **0.10** | 天气/距离/人数 |
| 探索度 | `S_explore` | **0.05** | 防茧房：给新菜一点机会 |
| | | **1.00** | |

> 💡 **为什么新鲜度(0.20) 高于偏好强度(0.15)？**
> 因为这是个**决策助手**，不是"猜你喜欢"。用户要的是"今天吃什么"，而不是"重复你最爱的那道"。若偏好强度过高，产品会退化成"永远推荐同一道菜"，用户很快会腻。新鲜度略高于偏好强度，能在"照顾口味"与"保持新鲜"之间取得平衡。这个比例是需要用真实数据调优的**首要参数**。

---

### 5.1 `S_taste` 口味匹配（w = 0.25）

```
若 MoodTags 为空：
    S_taste = 0.55 · S_spicy + 0.45 · S_cuisine
否则：
    S_taste = 0.45 · S_spicy + 0.30 · S_cuisine + 0.25 · S_mood
```

#### 5.1.1 `S_spicy` 辣度匹配

辣度不匹配是**非对称**的：不吃辣的人遇到中辣是灾难，吃辣的人遇到不辣只是有点淡。

```
基础公式：
    Δ_over  = max(0, dish.spicy - pref.spicy)     // 比偏好更辣
    Δ_under = max(0, pref.spicy - dish.spicy)     // 比偏好更淡
    S_spicy = 1 - 0.25 · (Δ_under / 5) - 0.75 · (Δ_over / 5)
            = 1 - 0.05 · Δ_under - 0.15 · Δ_over

低辣度敏感修正（安全护栏）：
    若 pref.spicy == 0 且 dish.spicy >= 3  →  S_spicy = 0.10
    若 pref.spicy == 1 且 dish.spicy >= 4  →  S_spicy = 0.20
```

**完整对照表**（行 = 用户偏好辣度，列 = 菜品辣度）

| pref \ dish | 0 | 1 | 2 | 3 | 4 | 5 |
|---|---|---|---|---|---|---|
| **0** | 1.00 | 0.85 | 0.70 | **0.10** | **0.10** | **0.10** |
| **1** | 0.95 | 1.00 | 0.85 | 0.70 | **0.20** | **0.20** |
| **2** | 0.90 | 0.95 | 1.00 | 0.85 | 0.70 | 0.55 |
| **3** | 0.85 | 0.90 | 0.95 | 1.00 | 0.85 | 0.70 |
| **4** | 0.80 | 0.85 | 0.90 | 0.95 | 1.00 | 0.85 |
| **5** | 0.75 | 0.80 | 0.85 | 0.90 | 0.95 | 1.00 |

> 加粗值 = 触发低辣度敏感修正。这保证了「完全不吃辣」的用户不会在转盘上抽到麻辣香锅。

#### 5.1.2 `S_cuisine` 菜系匹配

```
若 pref.preferredCuisines 为空  →  S_cuisine = 0.5   （中性，不惩罚也不加分）
否则若 dish.cuisine ∈ pref.preferredCuisines  →  S_cuisine = 1.0
否则  →  S_cuisine = 0.4
```

> 注意 `0.5` 与 `0.4` 的差别很小，这是刻意的：**用户没填偏好菜系时，不应该因此惩罚任何菜系**。

#### 5.1.3 `S_mood` 快捷需求匹配

用户在决策页勾选的快捷标签（「辣的」「清淡的」等）映射到菜品属性：

```csharp
private static readonly Dictionary<string, Func<DishCandidate, bool>> MoodMatchers = new()
{
    ["辣的"]   = d => d.SpicyLevel >= 3,
    ["清淡的"] = d => d.SpicyLevel <= 1 && !d.Tags.Contains("重口"),
    ["暖胃的"] = d => d.Tags.Contains("暖胃") || d.Category == DishCategory.Soup,
    ["快手"]   = d => d.CookMinutes is <= 20 || d.Tags.Contains("快手"),
    ["下饭"]   = d => d.Tags.Contains("下饭"),
    ["汤汤水水"] = d => d.Category == DishCategory.Soup,
    ["解腻"]   = d => d.Tags.Contains("清淡") || d.Category == DishCategory.Vegetable,
};

S_mood = 命中标签数 / MoodTags 总数
```

> 若某个标签在 `MoodMatchers` 中不存在，**该标签从分母中剔除**（不惩罚未知标签）。

---

### 5.2 `S_fresh` 新鲜度（w = 0.20）

核心思想：**刚吃过的菜要抑制，很久没吃的菜要恢复，吃过太多次的菜要额外抑制。**

#### 5.2.1 单维度公式

```
daysSince = (Now - lastEatenAt).TotalDays          // 无记录 → ∞
recencyFactor   = 1 - exp(-daysSince / τ)          // τ = 7 天（菜品）/ 10 天（菜系）
frequencyFactor = 1 / (1 + 0.3 · eatCount30d)      // 近 30 天食用次数

S = recencyFactor^0.6 · frequencyFactor^0.4        // 几何平均，两者都必须好
```

**数值验证**

| 场景 | recency | frequency | S |
|---|---|---|---|
| 从未吃过 | 1.000 | 1.000 | **1.000** |
| 30 天前吃过 1 次 | 0.986 | 1.000 | **0.992** |
| 14 天前吃过 1 次 | 0.865 | 1.000 | **0.918** |
| 7 天前吃过 1 次 | 0.632 | 0.769 | **0.683** |
| 3 天前吃过 1 次 | 0.349 | 0.769 | **0.499** |
| 昨天吃过 1 次 | 0.133 | 0.769 | **0.272** |
| 昨天吃过 3 次 | 0.133 | 0.526 | **0.233** |
| 今天刚吃过 1 次 | 0.000 | 0.769 | **0.000** |

#### 5.2.2 菜品级 + 菜系级合成

```
S_fresh_dish    = 用菜品级 stat 计算（τ = 7）
S_fresh_cuisine = 用菜系级 stat 计算（τ = 10，粒度更粗故更宽松）
S_fresh = 0.7 · S_fresh_dish + 0.3 · S_fresh_cuisine
```

> 💡 **为什么要菜系级？** 防止"连续三天吃川菜"——菜品不重复但口味单调。菜系级用更宽的 τ，避免惩罚过重。

---

### 5.3 `S_affinity` 偏好强度（w = 0.15）

```
若 ratingCount > 0:
    ratingScore = (avgRating - 1) / 4          // 1分→0, 5分→1
否则:
    ratingScore = 0.5                          // 中性

若 eatCount > 0:
    freqScore = min(1, ln(1 + eatCount) / ln(11))   // 10 次封顶
    wouldEatAgainRate = wouldEatAgainCount / eatCount
否则:
    freqScore = 0.3                            // 没吃过略低，但不为 0
    wouldEatAgainRate = 0.5                    // 中性

S_affinity = 0.5 · ratingScore + 0.3 · freqScore + 0.2 · wouldEatAgainRate
```

**数值验证**

| 场景 | rating | freq | again | S_affinity |
|---|---|---|---|---|
| 从未吃过 | 0.500 | 0.300 | 0.500 | **0.440** |
| 吃过 5 次，均分 4.5，3 次想再吃 | 0.875 | 0.747 | 0.600 | **0.782** |
| 吃过 10 次，均分 5.0，10 次想再吃 | 1.000 | 1.000 | 1.000 | **1.000** |
| 吃过 1 次，均分 2.0，不想再吃 | 0.250 | 0.289 | 0.000 | **0.212** |
| 吃过 3 次，未评分 | 0.500 | 0.578 | 0.500 | **0.523** |

#### 冷启动兜底（用户记录 < 3 条）

新用户没有个人历史，用**全局热度**兜底：

```
若 UserRecordCount < 3:
    S_affinity = 0.5 · S_affinity + 0.5 · (dish.Popularity / 100)
```

> `popularity` 是种子数据中人工标注的 0–100 热度分，代表"大众接受度"。这样新用户第一次用就能拿到合理结果。

---

### 5.4 `S_timeSlot` 时段契合（w = 0.15）

```
S_mealTime = 1.00  若 dish.MealTimes 含当前餐次
             0.10  否则

S_season   = 1.00  若 dish.Seasons == AllYear(0) 或含当前季节
             0.65  否则

S_timeSlot = 0.8 · S_mealTime + 0.2 · S_season
```

**数值验证**

| 场景 | S_mealTime | S_season | S_timeSlot |
|---|---|---|---|
| 午餐 + 番茄牛腩(Lunch\|Dinner, 四季) | 1.00 | 1.00 | **1.000** |
| 午餐 + 凉拌黄瓜(四季, 夏季) | 1.00 | 0.65 | **0.930** |
| 早餐 + 红烧肉(Lunch\|Dinner, 四季) | 0.10 | 1.00 | **0.280** |
| 夜宵 + 火锅(All, 冬季) | 1.00 | 0.65 | **0.930** |

**餐次推断规则**（`MealType` 为 null 时）

```csharp
public static MealType InferMealType(TimeOnly localTime) => localTime.Hour switch
{
    >= 5  and < 10 => MealType.Breakfast,
    >= 10 and < 15 => MealType.Lunch,
    >= 15 and < 21 => MealType.Dinner,
    _              => MealType.LateNight,   // 21:00–05:00
};
```

---

### 5.5 `S_budget` 预算匹配（w = 0.10）

```
dishPrice = (PriceMinCents + PriceMaxCents) / 2        若两者都有
          = PriceMinCents ?? PriceMaxCents              若只有一个
          = CategoryDefaultPrice[category]              若都没有（见下表）

budgetMin / budgetMax 取自请求覆盖值，否则画像值

若 budgetMax 为 null:                    // 用户未设预算
    S_budget = 0.5                       // 中性

否则若 budgetMin <= dishPrice <= budgetMax:
    S_budget = 1.0

否则若 dishPrice < budgetMin:            // 比预算便宜
    ratio = (budgetMin - dishPrice) / budgetMin
    S_budget = max(0.70, 1 - 0.30 · ratio)      // 最多降到 0.70

否则:                                     // 超预算
    over = (dishPrice - budgetMax) / budgetMax
    S_budget = max(0, 1 - 2 · over)             // 超 50% 即归零
```

**分类默认价格**（菜品无价格信息时兜底）

| 分类 | 默认价（分） | 折合 |
|---|---|---|
| 主食 Staple | 800 | ¥8 |
| 荤菜 Meat | 3800 | ¥38 |
| 素菜 Vegetable | 1800 | ¥18 |
| 汤羹 Soup | 2200 | ¥22 |
| 小吃 Snack | 1200 | ¥12 |
| 甜点 Dessert | 1500 | ¥15 |
| 饮品 Drink | 1000 | ¥10 |
| 早餐 Breakfast | 800 | ¥8 |
| 火锅烧烤 Hotpot | 8000 | ¥80 |

**数值验证**（budget = ¥20–¥50）

| 菜品价 | 计算 | S_budget |
|---|---|---|
| ¥38 | 区间内 | **1.00** |
| ¥15 | 1 - 0.3×(500/2000) = 1 - 0.075 | **0.925** |
| ¥8 | 1 - 0.3×(1200/2000) = 1 - 0.18 → max(0.7, 0.82) | **0.82** |
| ¥60 | over=0.2 → 1 - 0.4 | **0.60** |
| ¥75 | over=0.5 → 1 - 1.0 | **0.00** |

---

### 5.6 `S_context` 场景契合（w = 0.10）

多因素加权，**缺失维度自动重新归一化**（不给缺失项打低分）：

```
components = []   // (score, weight)

# ① 就餐方式
若 request.DiningMode != Whatever:
    components += (dish 支持该方式 ? 1.0 : 0.3, weight: 0.4)
否则:
    score = pref.DiningModeWeights 中该菜最适合方式的权重，无则 0.5
    components += (score, weight: 0.4)

# ② 距离（仅堂食/外卖 且 有定位）
若 request.Location != null 且 DiningMode ∈ {DineIn, Takeout}:
    若 dish.DistanceKm == null:
        components += (0.60, weight: 0.3)        // 未知，中性偏低
    否则:
        maxKm = pref.MaxDistanceM / 1000
        components += (clamp(1 - distanceKm / maxKm, 0, 1), weight: 0.3)

# ③ 天气
若 request.Weather != null:
    components += (WeatherScore(dish, weather), weight: 0.3)

# ④ 人数
若 request.PartySize > 1:
    components += (PartyScore(dish, partySize), weight: 0.2)

S_context = Σ(score · weight) / Σ(weight)         // 归一化
```

**天气打分表**

| 天气 | 加分（+） | 减分（−） |
|---|---|---|
| `rain` 下雨 | 汤羹/热食/堂食 (+0.2) | 凉菜/需外出 (-0.3) |
| `hot` 高温(>30°C) | 清淡/凉菜/饮品/素菜 (+0.2) | 重油/重辣/火锅 (-0.3) |
| `cold` 寒冷(<5°C) | 汤羹/火锅/炖菜/热食 (+0.3) | 凉菜/冷饮 (-0.4) |
| `snow` 下雪 | 同 `cold`，另加"外卖优先" | 同 `cold` |
| `clear` 晴 | 中性 0.6 | — |

```csharp
private static double WeatherScore(DishCandidate d, string weather)
{
    var baseScore = 0.6;   // 中性基准
    switch (weather)
    {
        case "rain":
            if (d.Category == DishCategory.Soup || d.Tags.Contains("热食")) baseScore += 0.2;
            if (d.Tags.Contains("凉菜")) baseScore -= 0.3;
            break;
        case "hot":
            if (d.SpicyLevel <= 1 && d.Category is DishCategory.Vegetable or DishCategory.Drink
                || d.Tags.Contains("清淡") || d.Tags.Contains("凉菜")) baseScore += 0.2;
            if (d.SpicyLevel >= 4 || d.Category == DishCategory.Hotpot) baseScore -= 0.3;
            break;
        case "cold":
        case "snow":
            if (d.Category is DishCategory.Soup or DishCategory.Hotpot
                || d.Tags.Contains("暖胃") || d.Tags.Contains("炖菜")) baseScore += 0.3;
            if (d.Tags.Contains("凉菜") || d.Category == DishCategory.Drink) baseScore -= 0.4;
            break;
    }
    return Math.Clamp(baseScore, 0, 1);
}
```

**人数打分表**

| 人数 | 加分菜型 | 减分菜型 |
|---|---|---|
| 1 人 | 快餐/小吃/单人份/快手 (+0.3) | 火锅/大份硬菜 (-0.4) |
| 2 人 | 家常菜/两菜一汤 (+0.2) | — |
| 3–4 人 | 硬菜/大份/多菜系 (+0.3) | 单人快餐 (-0.2) |
| 5+ 人 | 火锅/烧烤/聚餐菜 (+0.4) | 单人快餐 (-0.4) |

---

### 5.7 `S_explore` 探索度（w = 0.05）

```
若 eatCount == 0:      S_explore = 1.0     // 没试过，鼓励
否则若 eatCount <= 2:  S_explore = 0.7
否则:                  S_explore = 0.3     // 吃太多次了，给别的菜让位
```

权重仅 0.05，作用温和——只保证"偶尔给新菜一点机会"，不会强行推冷门菜。

---

## 6. 阶段 4：乘性惩罚

```
P_total = Π Pⱼ
```

| 代号 | 触发条件 | 乘数 | 说明 |
|---|---|---|---|
| `EATEN_24H` | `lastEatenAt` 距今 < 24 小时 | **0.10** | 几乎排除，但不硬删（极端情况仍需可选项） |
| `DISLIKED` | `ratingCount ≥ 1 && avgRating ≤ 2.0` | **0.25** | 「我不喜欢」的强信号 |
| `MEH` | `ratingCount ≥ 1 && avgRating ≤ 3.0` | **0.70** | 一般般，轻微抑制 |
| `NEVER_AGAIN` | `wouldEatAgain == false` 明确标记 | **0.30** | 用户明确说不想再吃 |

> ⚠️ **`DISLIKED` 与 `MEH` 互斥**（`avgRating ≤ 2` 时只应用 `DISLIKED`，不叠加）。

**叠加示例**

| 场景 | 乘数 | 100 分基准 → 实际 |
|---|---|---|
| 正常 | 1.00 | 85 → **85.0** |
| 12 小时前刚吃过 | 0.10 | 85 → **8.5** |
| 打过 1 分 | 0.25 | 85 → **21.3** |
| 12 小时前吃过 + 打过 1 分 | 0.025 | 85 → **2.1** |

---

## 7. 阶段 5：排除、抖动、排序

```csharp
// 1. 排除「换一批」时已展示过的
var pool = scored.Where(s => !request.ExcludeDishIds.Contains(s.DishId));

// 2. 可选抖动（仅「换一批」时启用，让结果有变化）
if (request.ExploreJitter > 0)
{
    foreach (var s in pool)
        s.Score += gaussian.NextGaussian() * request.ExploreJitter;   // σ 单位：分
}

// 3. 降序排序，分数相同按 DishId 稳定排序（保证确定性）
var ranked = pool
    .OrderByDescending(s => s.Score)
    .ThenBy(s => s.DishId)          // ← 关键：保证相同分数时结果稳定
    .Take(options.TopN.ResultList)  // 20
    .ToList();
```

> 💡 **确定性设计**：`ExploreJitter = 0` 时，相同输入必须产生**完全相同**的输出。这依赖 `ThenBy(DishId)` 打破平局。否则测试无法稳定通过。
> `Random` 必须用可注入种子的实现，测试中传固定种子。

---

## 8. 阶段 6：推荐理由生成

**取贡献值最大的 2 个维度**（贡献 = `weight × breakdown`），套用模板。

```csharp
private static readonly Dictionary<ScoreDimension, Func<ReasonContext, string?>> ReasonTemplates = new()
{
    [ScoreDimension.Freshness] = c =>
        c.S_fresh >= 0.85 && c.RecentCuisineCount >= 2
            ? $"你最近 {c.RecentDays} 天吃了 {c.RecentCuisineCount} 次{c.CuisineName}，今天换个花样"
            : c.S_fresh >= 0.85 ? "有阵子没吃这道了" : null,

    [ScoreDimension.Affinity] = c =>
        c.AvgRating >= 4.0 ? $"上次你给它打了 {c.AvgRating:0.#} 分"
      : c.EatCount >= 3    ? $"你吃过 {c.EatCount} 次，是老朋友了"
      : null,

    [ScoreDimension.Taste] = c =>
        c.S_spicy >= 0.9 ? $"{c.SpicyDesc}，正合你口味"
      : c.S_cuisine >= 1.0 ? $"你爱吃{c.CuisineName}"
      : null,

    [ScoreDimension.TimeSlot] = c =>
        c.S_mealTime >= 1.0 ? $"{c.MealTypeName}吃这个正合适" : null,

    [ScoreDimension.Budget] = c =>
        c.S_budget >= 1.0 ? $"¥{c.PriceYuan:0.#}，在你的预算内" : null,

    [ScoreDimension.Context] = c =>
        c.WeatherReason is not null ? c.WeatherReason
      : c.DistanceKm is not null && c.DistanceKm <= 1.0 ? $"就在附近，{c.DistanceKm * 1000:0} 米"
      : null,

    [ScoreDimension.Exploration] = c =>
        c.EatCount == 0 ? "你没试过这道，要不要尝尝" : null,
};
```

**理由质量规则**

1. **必须至少 1 条**。若所有模板都返回 null（例如全靠 popularity 兜底的新用户），使用兜底文案：「很多人都在吃」/「今天的推荐」。
2. **最多 2 条**。超过 2 条用户不读。
3. **优先级**：按贡献值降序尝试，取前 2 个非 null 结果。
4. **不出现负面表述**。绝不写「因为你上周吃了太多辣」这种指责性文案。

**输出示例**

```json
{
  "dishId": "3f2a…",
  "name": "番茄牛腩",
  "score": 92.4,
  "breakdown": {
    "taste": 0.88, "freshness": 0.95, "affinity": 0.90, "timeSlot": 1.00,
    "budget": 1.00, "context": 0.80, "exploration": 0.30
  },
  "penalties": [],
  "reasons": ["你最近 5 天吃了 3 次川菜，今天换个花样", "上次你给它打了 5 分"]
}
```

---

## 9. 完整算例

**用户画像**
- `spicyLevel = 2`（微辣）
- `budget = ¥20–¥50`（2000–5000 分）
- `avoidIngredients = ["香菜"]`
- `preferredCuisines = [川菜(1), 粤菜(2)]`
- `maxDistance = 2000m`

**本次请求**
- `mealType = Lunch`（午餐）
- `diningMode = DineIn`（堂食）
- `partySize = 1`
- `weather = "rain"`（下雨）
- `moodTags = []`
- `now = 2025-06-15 12:00 UTC`

**候选菜品：番茄牛腩**

| 属性 | 值 |
|---|---|
| cuisine | 川菜(1) |
| category | 荤菜(2) |
| spicyLevel | 1 |
| price | 3200–4800 分 → 均价 4000 |
| mealTimes | Lunch \| Dinner (6) |
| seasons | AllYear (0) |
| ingredients | 牛腩、番茄、洋葱 |
| tags | 下饭、暖胃、炖菜 |
| hasRecipe | true |
| cookMinutes | 90 |
| popularity | 88 |
| distanceKm | 0.8 |

**用户历史**：吃过 4 次，均分 4.5（ratingSum=18, ratingCount=4），3 次标记想再吃，最近一次 12 天前

**逐步计算**

| 维度 | 计算过程 | Sᵢ | wᵢ | 贡献 |
|---|---|---|---|---|
| `S_spicy` | pref=2, dish=1 → 1 - 0.05×1 = 0.95 | 0.95 | | |
| `S_cuisine` | 川菜 ∈ 偏好 → 1.00 | 1.00 | | |
| **`S_taste`** | 0.55×0.95 + 0.45×1.00 = 0.5225 + 0.45 | **0.9725** | 0.25 | 0.2431 |
| `S_fresh_dish` | daysSince=12 → recency=1-e^(-12/7)=0.8195；eatCount30d=0 → freq=1.0；0.8195^0.6×1.0^0.4 = 0.8868 | 0.8868 | | |
| `S_fresh_cuisine` | 假设最近 5 天吃过 3 次川菜，最近一次 2 天前 → recency=1-e^(-2/10)=0.1813；freq=1/(1+0.3×3)=0.5263；0.1813^0.6×0.5263^0.4 = 0.3576×0.7759 = 0.2775 | 0.2775 | | |
| **`S_fresh`** | 0.7×0.8868 + 0.3×0.2775 = 0.6208 + 0.0833 | **0.7041** | 0.20 | 0.1408 |
| `ratingScore` | (4.5-1)/4 = 0.875 | | | |
| `freqScore` | ln(5)/ln(11) = 1.6094/2.3979 = 0.6712 | | | |
| `wouldEatAgainRate` | 3/4 = 0.75 | | | |
| **`S_affinity`** | 0.5×0.875 + 0.3×0.6712 + 0.2×0.75 = 0.4375+0.2014+0.15 | **0.7889** | 0.15 | 0.1183 |
| `S_mealTime` | Lunch ∈ {Lunch,Dinner} → 1.00 | | | |
| `S_season` | AllYear → 1.00 | | | |
| **`S_timeSlot`** | 0.8×1.00 + 0.2×1.00 | **1.0000** | 0.15 | 0.1500 |
| **`S_budget`** | 4000 ∈ [2000,5000] → 1.00 | **1.0000** | 0.10 | 0.1000 |
| 就餐方式 | DineIn，支持 → 1.00 (w=0.4) | | | |
| 距离 | 1 - 0.8/2.0 = 0.60 (w=0.3) | | | |
| 天气 rain | 暖胃/汤类 → 0.6+0.2 = 0.80 (w=0.3) | | | |
| 人数 1 | 不是快餐/火锅，中性 0.6 (w=0.2) | | | |
| **`S_context`** | (1.00×0.4 + 0.60×0.3 + 0.80×0.3 + 0.60×0.2) / 1.2 = (0.4+0.18+0.24+0.12)/1.2 = 0.94/1.2 | **0.7833** | 0.10 | 0.0783 |
| **`S_explore`** | eatCount=4 > 2 → 0.30 | **0.3000** | 0.05 | 0.0150 |

```
TotalScore = 0.2431 + 0.1408 + 0.1183 + 0.1500 + 0.1000 + 0.0783 + 0.0150
           = 0.8455

惩罚项：
  EATEN_24H?   最近 12 天前 → 不触发
  DISLIKED?    avgRating 4.5 → 不触发
  NEVER_AGAIN? 3/4 想再吃，未标记 false → 不触发
  P_total = 1.0

FinalScore = 0.8455 × 1.0 = 0.8455
Score100   = 84.6
```

**生成的理由**（按贡献降序）

| 维度 | 贡献 | 模板结果 |
|---|---|---|
| Taste | 0.2431 | `S_spicy=0.95 < 0.9`? 否，0.95 ≥ 0.9 → 「微辣，正合你口味」 |
| TimeSlot | 0.1500 | 「午餐吃这个正合适」 |
| Freshness | 0.1408 | `S_fresh=0.70 < 0.85` → 返回 null |

取前 2 个非 null：**「微辣，正合你口味」+「午餐吃这个正合适」**

> 💡 注意这里 `S_fresh` 因为菜系重复被拉低到 0.70，所以"换个花样"的文案没触发——**逻辑是自洽的**。

---

## 10. 冷启动策略

| 场景 | 处理 |
|---|---|
| **无画像**（未做偏好题） | 用 `UserPreferenceSnapshot.Default`（辣度 2、¥15–50、无忌口、无偏好菜系） |
| **无历史记录** | `S_fresh = 1.0`（全员）、`S_affinity = 0.44`（全员）；靠 `taste/timeSlot/budget` + **popularity 兜底**区分 |
| **用户记录 < 3 条** | `S_affinity` 混合 50% 全局热度 |
| **候选集 < 8** | 见下方降级链 |
| **候选集 = 0** | 返回空列表 + 明确提示「没有符合条件的菜品，试试放宽条件」；**绝不返回随机垃圾** |

### 候选集不足的降级链

```
Level 0（正常）：全部硬过滤 + 时段过滤
    ↓ 候选 < 8
Level 1：放宽时段（保留忌口、就餐方式）
    ↓ 候选 < 8
Level 2：放宽距离限制
    ↓ 候选 < 8
Level 3：放宽就餐方式
    ↓ 候选 < 8
Level 4：仅保留忌口过滤（安全底线，永不放开）
```

> ⚠️ **忌口过滤在任何降级层级都不放开。** 宁可返回"没有合适的菜"，也不能推荐过敏原。

---

## 11. 配置化参数

全部参数外置，**不发版即可调优**：

```jsonc
// appsettings.json → DecisionEngine
{
  "DecisionEngine": {
    "Version": "1.0.0",

    "Weights": {
      "Taste":       0.25,
      "Freshness":   0.20,
      "Affinity":    0.15,
      "TimeSlot":    0.15,
      "Budget":      0.10,
      "Context":     0.10,
      "Exploration": 0.05
    },

    "Freshness": {
      "DishTauDays":    7.0,
      "CuisineTauDays": 10.0,
      "DishWeight":     0.7,
      "CuisineWeight":  0.3,
      "RecencyPower":   0.6,
      "FrequencyPower": 0.4,
      "FrequencyAlpha": 0.3
    },

    "Affinity": {
      "FreqCap":              10,
      "DefaultRatingScore":   0.5,
      "DefaultFreqScore":     0.3,
      "DefaultAgainRate":     0.5
    },

    "Penalties": {
      "EatenWithin24h": 0.10,
      "Disliked":       0.25,
      "DislikedThreshold": 2.0,
      "Meh":            0.70,
      "MehThreshold":   3.0,
      "NeverAgain":     0.30
    },

    "ColdStart": {
      "MinRecordsForAffinity": 3,
      "PopularityBlend":       0.5
    },

    "TopN": {
      "Wheel":       8,
      "ResultList":  20,
      "DisplayList": 5
    },

    "Jitter": {
      "DefaultSigma": 0,
      "RefreshSigma": 3.0
    }
  }
}
```

**权重校验**：启动时断言 `Σ Weights == 1.0`，否则抛异常快速失败。

```csharp
public sealed class EngineOptions
{
    public required ScoreWeights Weights { get; init; }
    // ...

    public void Validate()
    {
        var sum = Weights.Taste + Weights.Freshness + Weights.Affinity
                + Weights.TimeSlot + Weights.Budget + Weights.Context
                + Weights.Exploration;

        if (Math.Abs(sum - 1.0) > 1e-6)
            throw new InvalidOperationException(
                $"决策引擎权重之和必须为 1.0，当前为 {sum:F4}");
    }
}
```

---

## 12. 实现骨架

```csharp
// FoodMate.Core/Decision/DecisionEngine.cs
namespace FoodMate.Core.Decision;

public interface IDecisionEngine
{
    DecisionResult Decide(DecisionContext context);
}

public sealed class DecisionEngine : IDecisionEngine
{
    private readonly IReadOnlyList<IScorer> _scorers;
    private readonly IPenaltyEvaluator _penalties;
    private readonly IReasonGenerator _reasons;
    private readonly IRandomSource _random;

    public DecisionEngine(
        IEnumerable<IScorer> scorers,
        IPenaltyEvaluator penalties,
        IReasonGenerator reasons,
        IRandomSource random)
    {
        _scorers = scorers.ToList();
        _penalties = penalties;
        _reasons = reasons;
        _random = random;
    }

    public DecisionResult Decide(DecisionContext ctx)
    {
        var sw = ValueStopwatch.StartNew();

        // ── 阶段 1：硬过滤 ──────────────────────────────
        var filtered = HardFilter.Apply(ctx);
        var filteredOut = ctx.Candidates.Count - filtered.Count;

        // ── 阶段 2：保底降级 ────────────────────────────
        filtered = FallbackRelaxer.EnsureMinimum(
            filtered, ctx, minCount: ctx.Options.TopN.Wheel);

        // ── 阶段 3–4：打分 + 惩罚 ───────────────────────
        var scored = new List<ScoredDish>(filtered.Count);
        foreach (var dish in filtered)
        {
            var breakdown = new Dictionary<ScoreDimension, double>(7);
            double total = 0;

            foreach (var scorer in _scorers)
            {
                var s = Math.Clamp(scorer.Score(dish, ctx), 0, 1);
                breakdown[scorer.Dimension] = s;
                total += ctx.Options.Weights[scorer.Dimension] * s;
            }

            var penalties = _penalties.Evaluate(dish, ctx);
            var final = penalties.Aggregate(total, (acc, p) => acc * p.Multiplier);

            scored.Add(new ScoredDish(
                dish.Id, dish.Name, final * 100, breakdown, penalties, []));
        }

        // ── 阶段 5：排除 + 抖动 + 排序 ──────────────────
        var pool = scored.Where(s => !ctx.Request.ExcludeDishIds.Contains(s.DishId));

        if (ctx.Request.ExploreJitter > 0)
        {
            pool = pool.Select(s => s with
            {
                Score = s.Score + _random.NextGaussian() * ctx.Request.ExploreJitter
            });
        }

        var ranked = pool
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DishId)
            .Take(ctx.Options.TopN.ResultList)
            .ToList();

        // ── 阶段 6：生成理由 ────────────────────────────
        ranked = ranked
            .Select(s => s with { Reasons = _reasons.Generate(s, ctx) })
            .ToList();

        return new DecisionResult(
            ranked,
            CandidateCount: filtered.Count,
            FilteredOutCount: filteredOut,
            EngineVersion: ctx.Options.Version,
            ElapsedMs: sw.ElapsedMilliseconds);
    }
}
```

```csharp
// FoodMate.Core/Decision/Scoring/IScorer.cs
public interface IScorer
{
    ScoreDimension Dimension { get; }
    double Score(DishCandidate dish, DecisionContext ctx);
}
```

> 💡 **Scorer 通过 DI 注册**，`DecisionEngine` 只依赖 `IEnumerable<IScorer>`。新增维度无需改动引擎代码。

---

## 13. 测试用例

### 13.1 单元测试（Scorer 级）

| # | 用例 | 输入 | 期望 |
|---|---|---|---|
| T01 | 辣度完全匹配 | pref=2, dish=2 | `S_spicy == 1.0` |
| T02 | 不吃辣遇重辣被护栏拦截 | pref=0, dish=5 | `S_spicy == 0.10` |
| T03 | 吃辣遇清淡仅轻微降分 | pref=5, dish=0 | `S_spicy == 0.75` |
| T04 | 菜系偏好为空时中性 | pref=[], dish=任意 | `S_cuisine == 0.5` |
| T05 | 从未吃过的新菜满分新鲜度 | 无 stat | `S_fresh == 1.0` |
| T06 | 昨天吃过新鲜度被抑制 | lastEaten=昨天 | `S_fresh < 0.35` |
| T07 | 今天刚吃过归零 | lastEaten=2h前 | `S_fresh_dish == 0.0` |
| T08 | 差评拉低偏好强度 | 均分 1.5，1 次 | `S_affinity < 0.25` |
| T09 | 时段不匹配被惩罚 | 早餐 + 仅午晚餐菜 | `S_timeSlot == 0.28` |
| T10 | 超预算 50% 归零 | budget max=5000, dish=7500 | `S_budget == 0.0` |
| T11 | 预算内满分 | dish=4000 ∈ [2000,5000] | `S_budget == 1.0` |
| T12 | 场景缺失维度重新归一化 | 无定位、无天气、1 人 | `S_context == 就餐方式分` |
| T13 | 探索度随次数递减 | 0次/2次/5次 | `1.0 / 0.7 / 0.3` |

### 13.2 引擎级测试

| # | 用例 | 输入 | 期望 |
|---|---|---|---|
| T20 | **忌口硬过滤** | `avoid=[花生]`，3 道菜含花生 | 含花生的菜**不出现在结果中** |
| T21 | **过敏原硬过滤** | `isCommonAllergen=true` 且命中 | 同上 |
| T22 | 忌口同义词展开 | `avoid=[坚果]`，菜含「腰果」 | 被过滤 |
| T23 | 24 小时惩罚 | 同一道菜 12h 前吃过 | 该菜 `Score < 10` |
| T24 | 排除列表生效 | `exclude=[X]` | X 不在结果中 |
| T25 | **确定性** | 相同输入调用 2 次 | 结果**完全一致**（含顺序） |
| T26 | 抖动可复现 | 固定种子的 `IRandomSource` | 两次结果一致 |
| T27 | 候选不足时降级 | 严格过滤后仅 3 道 | 结果 ≥ 8 道，且**忌口仍生效** |
| T28 | 候选为零 | 全部被过滤 | 返回空列表，不抛异常 |
| T29 | 理由非空 | 任意正常输入 | 每个结果 `Reasons.Count >= 1` |
| T30 | 理由不超过 2 条 | 任意输入 | `Reasons.Count <= 2` |
| T31 | 权重和为 1 | 加载配置 | `Validate()` 不抛异常 |
| T32 | 权重和不为 1 快速失败 | 篡改配置 | `Validate()` 抛 `InvalidOperationException` |
| T33 | 性能 | 2000 候选 | `ElapsedMs < 50` |

### 13.3 测试示例代码

```csharp
public class HardFilterTests
{
    [Fact]
    public void 含忌口食材的菜品必须被排除()
    {
        // Arrange
        var peanutDish = BuildDish("宫保鸡丁",
            ingredients: [new Ingredient("鸡丁", "300g", false),
                          new Ingredient("花生米", "50g", true)]);
        var safeDish = BuildDish("番茄炒蛋",
            ingredients: [new Ingredient("番茄", "2个", false),
                          new Ingredient("鸡蛋", "3个", true)]);

        var ctx = BuildContext(
            candidates: [peanutDish, safeDish],
            avoidIngredients: ["花生"]);

        // Act
        var result = new DecisionEngine(/* ... */).Decide(ctx);

        // Assert
        result.Ranked.Should().NotContain(r => r.DishId == peanutDish.Id);
        result.Ranked.Should().Contain(r => r.DishId == safeDish.Id);
    }

    [Fact]
    public void 忌口同义词应能匹配()
    {
        var cashewDish = BuildDish("腰果虾仁",
            ingredients: [new Ingredient("腰果", "80g", true)]);

        var ctx = BuildContext(candidates: [cashewDish], avoidIngredients: ["坚果"]);
        var result = new DecisionEngine(/* ... */).Decide(ctx);

        result.Ranked.Should().BeEmpty();
    }

    [Fact]
    public void 候选为零时不应抛异常()
    {
        var ctx = BuildContext(candidates: [], avoidIngredients: []);
        var act = () => new DecisionEngine(/* ... */).Decide(ctx);

        act.Should().NotThrow();
        act().Ranked.Should().BeEmpty();
    }
}

public class DeterminismTests
{
    [Fact]
    public void 相同输入必须产生完全相同的结果()
    {
        var ctx = BuildContext(/* 固定数据 */);
        var engine = new DecisionEngine(/* ExploreJitter = 0 */);

        var first  = engine.Decide(ctx);
        var second = engine.Decide(ctx);

        first.Ranked.Select(r => (r.DishId, r.Score))
             .Should().Equal(second.Ranked.Select(r => (r.DishId, r.Score)));
    }
}

public class SpicyScoringTests
{
    [Theory]
    [InlineData(0, 5, 0.10)]   // 不吃辣遇重辣 → 护栏
    [InlineData(0, 3, 0.10)]   // 不吃辣遇中辣 → 护栏
    [InlineData(0, 2, 0.70)]   // 不吃辣遇微辣 → 基础公式
    [InlineData(2, 2, 1.00)]   // 完全匹配
    [InlineData(5, 0, 0.75)]   // 吃辣遇清淡 → 轻微降分
    [InlineData(4, 5, 0.85)]   // 略辣一点
    public void 辣度打分应符合对照表(short pref, short dish, double expected)
    {
        var ctx = BuildContext(spicyLevel: pref);
        var scorer = new TasteScorer();

        var actual = scorer.ScoreSpicy(dishSpicy: dish, ctx);

        actual.Should().BeApproximately(expected, 1e-6);
    }
}
```

---

## 14. 演进路线

| 阶段 | 时间 | 做法 | 数据依赖 |
|---|---|---|---|
| **v1.0（当前）** | M1 | 固定权重 + 规则打分 | 无 |
| **v1.1** | 上线 2 周后 | 用 `decision_sessions` 分析「推荐了但没选」的模式，**人工调权重** | ~100 次决策 |
| **v1.2** | 上线 1 月后 | 引入**分时段权重**（早餐与晚餐用不同权重表） | ~1000 次决策 |
| **v2.0** | 上线 3 月后 | 逻辑回归 / Learning-to-Rank，特征即 7 个维度分 + 上下文 | ~5000 次决策 + 标签 |
| **v3.0** | 用户量足够后 | 协同过滤 / 向量召回（菜品 embedding） | ~1 万用户 |

### v1.1 调权重的具体方法

`decision_sessions` 表记录了每次推荐的 Top 20（含明细）与用户最终选择。可以计算：

```
指标 1：采纳率（Acceptance Rate）
    = 有 chosen_dish_id 的会话数 / 总会话数
    目标：> 60%

指标 2：Top1 命中率
    = chosen_dish_id == ranked[0] 的比例
    目标：> 35%

指标 3：排名偏移（Rank Offset）
    = chosen 在候选中的排名的均值
    若均值 > 3，说明 Top1 打分严重失真
```

**调参方法**：对权重做小步网格搜索，用历史决策数据回放（replay），选择让 Top1 命中率最大的权重组合。

```csharp
// 离线回放工具（控制台程序）
foreach (var weightSet in GridSearch(step: 0.05))
{
    var hitRate = ReplaySessions(sessions, weightSet);
    results.Add((weightSet, hitRate));
}
var best = results.MaxBy(r => r.hitRate);
```

> 💡 **这是本产品最值得投入的优化方向**。权重调好了，用户体验的提升远大于任何 UI 打磨。

---

## 15. 待确认问题

| # | 问题 | 影响 |
|---|---|---|
| Q1 | 默认权重是否认可？尤其「新鲜度 0.20 > 偏好强度 0.15」这个取舍 | 决定产品调性：偏"探索"还是偏"稳妥" |
| Q2 | 「不吃辣」的护栏阈值（pref=0 且 dish≥3 → 0.10）是否合适？ | 辣度是中式餐饮最敏感的维度 |
| Q3 | 是否需要「我不想吃 XX」的临时排除（一次性，不写入画像）？ | 需在请求中加 `ExcludeCuisines` 字段 |
| Q4 | 转盘是否要支持「再来一次」但**不改变**候选（纯动画重转）？ | 影响前端交互与引擎调用次数 |
| Q5 | 是否需要「随便，但别太贵」这类自然语言输入？ | 需要 NLU 或大模型解析，v1 用快捷标签替代 |

---

## 16. M1 实现说明（与本文档的差异）

引擎已落地（`FoodMate.Core/Decision/`），以下实现细节与前述章节有出入，**以实际代码为准**：

| 项 | 文档示意 | 实际实现 | 原因 |
|---|---|---|---|
| 降级链 | 4 级（时段 → 距离 → 就餐方式 → 仅保留忌口） | **2 级**（放宽就餐方式 → 接受现状） | 实施时发现**只有就餐方式是硬过滤**：时段与距离被移到了打分环节，因为它们属于「不太合适」而非「不能吃」。这是更好的分工。忌口在任何层级都不放开 |
| 理由选择 | 按「权重 × 归一化分」取前 2 | 按「权重 × 归一化分 × **信息量系数**」取前 2 | 见下方「发现的问题 2」 |
| `NEVER_AGAIN` 判定 | 未明确 | `WouldNotEatAgainCount > 0 && WouldEatAgainCount == 0` | 需要区分「没表态」与「明确说不想再吃」，故给 `user_dish_stats` 增加了 `would_not_eat_again_count` 列 |
| 近 30 天频次 | 从统计表读 | 从 `meal_records` **现算** | `user_dish_stats` 只有累计次数。用「两个简单查询 + 内存聚合」实现，避免 EF 无法翻译的 `Join`（可空外键与主键类型不匹配会产生转换节点） |
| 距离维度 | 参与打分 | **恒为 null**（中性 0.60） | v1 无门店/商家数据，无从计算距离。接入门店数据后启用 |

### 发现的问题 1：SQLite 不支持 `DateTimeOffset` 的比较与排序

**现象**：`Where(r => r.EatenAt >= since)` 在运行时抛
`The LINQ expression could not be translated`。

**根因**：EF Core 的 SQLite Provider 不支持 `DateTimeOffset` 的比较与排序操作。
而这正是 `S_fresh` 的核心查询（取用户近 30 天记录）。

**解决**：在 `OnModelCreating` 中按 Provider 条件化地注册值转换器
（`FoodMateDbContext.ApplySqliteDateTimeOffsetWorkaround`）：

- **SQLite** → 存为定宽 ISO-8601 UTC 字符串 `yyyy-MM-ddTHH:mm:ss.fffffffZ`，
  字典序即时间序，`>=` 与 `OrderBy` 都能翻译。
- **PostgreSQL** → 仍映射原生 `timestamptz`，不受影响。

**为什么不用 EF Core 的默认格式**：默认格式是 `yyyy-MM-dd HH:mm:ss.fffffff+HH:mm`，
**偏移量不一致时字典序会错乱**。这里强制转 UTC 并定宽输出，保证排序正确。

**验证**：`tests/FoodMate.Infrastructure.Tests/DateTimeOffsetOrderingTests.cs`
用 5 个测试守住这条不变量——不同偏移量的同一时刻存为同一值、范围过滤条数正确、
排序与时间序一致、毫秒级差异可区分、往返后保持 UTC。

### 发现的问题 2：理由被「时段」维度淹没

**现象**：几乎所有推荐的理由都是「午餐吃这个正合适 / ¥38，在你的预算内」，
而「上次你给它打了 4.5 分」这类真正有说服力的理由从不出现。

**根因**：时段对几乎每道候选菜都满分（只要它适合当前餐次），
于是 `权重 × 归一化分` 稳定排在前列，把个人化理由挤掉了。

**解决**：引入**信息量系数** `ReasonPriority`，在贡献值之上再乘一层：

| 维度 | 系数 | 理由 |
|---|---|---|
| 偏好强度 | 1.6 | 个人历史最有说服力 |
| 新鲜度 | 1.3 | |
| 场景契合 | 1.2 | 天气/距离很具体 |
| 探索度 | 1.1 | |
| 口味匹配 | 1.0 | |
| 预算匹配 | 0.7 | 很常见，信息量低 |
| 时段合适 | **0.6** | 几乎总是命中 |

**效果**：番茄牛腩的算例从 `["午餐吃这个正合适", "¥40，在你的预算内"]`
变为 `["微辣，正合你口味", "上次你给它打了 4.5 分"]`。

### ⚠️ 发现的问题 3：冷启动分数压缩（**待调优**）

**现象**：新用户（无画像、无历史）的 Top 6 分数挤在 83 分左右：

```
1. 西红柿炒蛋  83.6
2. 青椒土豆丝  83.4
3. 番茄鸡蛋面  83.3
4. 蛋炒饭      83.1
5. 番茄牛腩    82.8
6. 酸辣汤      82.7
```

**根因**：冷启动时多个维度对**所有**候选都取同一个值，失去区分能力：

| 维度 | 冷启动取值 | 原因 |
|---|---|---|
| `S_cuisine` | 恒 0.5 | 用户未填偏好菜系 |
| `S_fresh` | 恒 1.0 | 无历史记录 |
| `S_timeSlot` | 多数 1.0 | 大部分菜适用当前餐次 |
| `S_budget` | 多数 1.0 | 价格普遍落在默认预算区间内 |

真正有区分度的只剩偏好强度（热度兜底，有效权重 0.15 × 0.5 = 0.075）
与场景契合（权重 0.10）。

**判断**：这**不是正确性问题**——20 道菜的全量分数区间是 70.8–83.6，
排序本身有效（热度高的菜确实排在前面），只是**头部被压平**。

**候选调优方向**（按推荐优先级）：

1. **前端改为展示匹配度而非原始分** —— 把 Top1 归一化为 100%，
   或直接用定性标签（强烈推荐 / 推荐 / 可以考虑）。成本最低，立即改善观感。
2. **提高冷启动热度混合比例** —— `ColdStart.PopularityBlend` 从 0.5 提到 0.7。
3. **无信息维度做权重再归一化** —— 当 `preferredCuisines` 为空时，
   把 `S_cuisine` 的 0.45 子权重按比例分给 `S_spicy` 与热度。
   这与 `ContextScorer` 已有的「缺失维度重新归一化」思路一致。
4. **等真实使用数据再调** —— 用 `decision_sessions` 的采纳率与排名偏移做依据（见 §14）。

**建议**：先做 1（前端展示层），其余等 M2 有了真实记录数据再调。
过早基于 20 道菜的假设数据调权重，容易过拟合。

### 测试覆盖

共 **171 个测试**（146 单测 + 25 集成测试），全部通过。

| 测试文件 | 数量 | 覆盖内容 |
|---|---|---|
| `Core.Tests/Decision/ScorerTests.cs` | 70 | 7 个 Scorer 的公式与边界，含**完整辣度对照表**（30 组 InlineData） |
| `Core.Tests/Decision/DecisionEngineTests.cs` | 37 | 硬过滤、惩罚、确定性、抖动可复现、降级、理由、性能、**§9 完整算例** |
| `Core.Tests/MealTimesTests.cs` | 39 | 餐次推断边界、枚举标签、画像权重、菜品快照 |
| `Infrastructure.Tests/MealRecordQueryTests.cs` | 10 | EF 查询翻译 + JSON 列往返与变更追踪 |
| `Infrastructure.Tests/DateTimeOffsetOrderingTests.cs` | 5 | 时间序正确性（见「发现的问题 1」） |
| `Infrastructure.Tests/DecisionServiceTests.cs` | 10 | 数据库 → 应用服务 → 引擎全链路，含忌口端到端生效 |

其中 `完整算例_番茄牛腩应得分约84点6` 逐维度核对了本文档 §9 的手工推导结果
（`S_taste=0.9725`、`S_fresh≈0.7048`、`S_affinity=0.7889`、`S_context=0.7833`…），
总分落在 `[84.0, 85.2]`。

**性能实测**：2000 候选 × 7 维度打分耗时 **< 50ms**（有断言守护），
实测端到端接口耗时约 30ms（含数据库读取与决策会话落库）。
