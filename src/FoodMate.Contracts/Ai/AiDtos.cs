namespace FoodMate.Contracts.Ai;

/// <summary>菜品识别请求。</summary>
public sealed record RecognizeRequest
{
    /// <summary>图片地址（<c>/uploads/...</c> 相对路径或 http(s) 绝对地址）。</summary>
    public string ImageUrl { get; init; } = string.Empty;
}

/// <summary>识别出的一道菜。</summary>
public sealed record RecognizedItemDto
{
    /// <summary>序号，确认时按此回传。</summary>
    public int Index { get; init; }

    /// <summary>模型给出的菜名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>置信度 0–1。</summary>
    public double Confidence { get; init; }

    /// <summary>是否需要用户重点复核（置信度 &lt; 0.70）。</summary>
    public bool NeedsReview { get; init; }

    /// <summary>估算热量（kcal）。</summary>
    public int? EstimatedCalories { get; init; }

    /// <summary>模型认为的主要食材。</summary>
    public IReadOnlyList<string> Ingredients { get; init; } = [];

    /// <summary>份量系数。</summary>
    public double Portion { get; init; } = 1.0;

    /// <summary>匹配到的菜品库 ID；未匹配上为 null。</summary>
    public Guid? MatchedDishId { get; init; }

    /// <summary>匹配到的菜品正式名。</summary>
    public string? MatchedDishName { get; init; }

    /// <summary>匹配置信度。</summary>
    public double? MatchScore { get; init; }

    /// <summary>是否通过别名命中。</summary>
    public bool MatchedViaAlias { get; init; }

    /// <summary>与用户忌口冲突的食材；非空时前端应高亮警告。</summary>
    public IReadOnlyList<string> ConflictIngredients { get; init; } = [];
}

/// <summary>菜品识别响应。</summary>
public sealed record RecognizeResponse
{
    /// <summary>识别日志 ID，确认时必传。</summary>
    public Guid LogId { get; init; }

    /// <summary>使用的模型名。</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>
    /// 是否为桩模型。
    /// </summary>
    /// <remarks>
    /// 为 <c>true</c> 时结果<b>与图片内容无关</b>，仅供本地跑通流程。
    /// 前端应显示明确提示，避免被误认为真实识别能力。
    /// </remarks>
    public bool IsStubModel { get; init; }

    /// <summary>模型耗时（毫秒）。</summary>
    public long LatencyMs { get; init; }

    /// <summary>场景：dine_in / takeout / homemade / unknown。</summary>
    public string Scene { get; init; } = "unknown";

    /// <summary>场景中文名。</summary>
    public string SceneLabel { get; init; } = "未知";

    /// <summary>整体置信度。</summary>
    public double OverallConfidence { get; init; }

    /// <summary>识别出的菜品。</summary>
    public IReadOnlyList<RecognizedItemDto> Items { get; init; } = [];

    /// <summary>今日剩余识别次数。</summary>
    public int RemainingToday { get; init; }
}

/// <summary>确认时提交的单道菜。</summary>
public sealed record ConfirmItemRequest
{
    /// <summary>对应识别结果的序号。</summary>
    public int Index { get; init; }

    /// <summary>关联的菜品库 ID；为空表示新建自定义菜品。</summary>
    public Guid? DishId { get; init; }

    /// <summary>用户确认或修正后的菜名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>份量系数。</summary>
    public double Portion { get; init; } = 1.0;

    /// <summary>用户修正后的热量估算。</summary>
    public int? EstimatedCalories { get; init; }
}

/// <summary>确认识别结果并入库。</summary>
public sealed record ConfirmRecognitionRequest
{
    /// <summary>餐次。</summary>
    public short MealType { get; init; }

    /// <summary>就餐方式。</summary>
    public short DiningMode { get; init; }

    /// <summary>就餐时间；不传取服务端当前时间。</summary>
    public DateTimeOffset? EatenAt { get; init; }

    /// <summary>确认后的菜品列表。</summary>
    public IReadOnlyList<ConfirmItemRequest> Items { get; init; } = [];

    /// <summary>遇到忌口冲突时是否强制写入。</summary>
    public bool Force { get; init; }
}

/// <summary>确认结果。</summary>
public sealed record ConfirmRecognitionResponse
{
    /// <summary>创建的饮食记录 ID。</summary>
    public IReadOnlyList<Guid> RecordIds { get; init; } = [];

    /// <summary>新建的自定义菜品 ID。</summary>
    public IReadOnlyList<Guid> CreatedDishIds { get; init; } = [];

    /// <summary>用户修正过的条目数。</summary>
    public int CorrectedCount { get; init; }

    /// <summary>本次识别是否被修正过（写入 <c>ai_recognition_logs.is_corrected</c>）。</summary>
    public bool WasCorrected { get; init; }
}

/// <summary>生成菜谱请求。</summary>
public sealed record GenerateRecipeRequest
{
    /// <summary>菜名。</summary>
    public string DishName { get; init; } = string.Empty;

    /// <summary>菜品 ID；传了则结果会缓存到菜品上。</summary>
    public Guid? DishId { get; init; }

    /// <summary>份数。</summary>
    public short Servings { get; init; } = 2;
}

/// <summary>菜谱步骤。</summary>
public sealed record RecipeStepDto(int Order, string Text, int? DurationMinutes);

/// <summary>菜谱。</summary>
public sealed record RecipeDto
{
    /// <summary>菜品 ID。</summary>
    public Guid? DishId { get; init; }

    /// <summary>菜名。</summary>
    public string DishName { get; init; } = string.Empty;

    /// <summary>份数。</summary>
    public short Servings { get; init; }

    /// <summary>总时长（分钟）。</summary>
    public short CookMinutes { get; init; }

    /// <summary>难度 1–3。</summary>
    public short Difficulty { get; init; }

    /// <summary>难度中文名。</summary>
    public string DifficultyLabel { get; init; } = string.Empty;

    /// <summary>步骤。</summary>
    public IReadOnlyList<RecipeStepDto> Steps { get; init; } = [];

    /// <summary>小贴士。</summary>
    public string? Tips { get; init; }

    /// <summary>是否来自缓存（菜品库里已有菜谱）。</summary>
    public bool FromCache { get; init; }

    /// <summary>是否由桩模型生成。</summary>
    public bool IsStubModel { get; init; }
}

/// <summary>买菜清单请求。</summary>
public sealed record ShoppingListRequest
{
    /// <summary>菜品 ID 列表。</summary>
    public IReadOnlyList<Guid> DishIds { get; init; } = [];

    /// <summary>份数。</summary>
    public short Servings { get; init; } = 2;
}

/// <summary>买菜清单项。</summary>
public sealed record ShoppingItemDto(string Name, string? TotalAmount, string? Category, IReadOnlyList<string> ForDishes);

/// <summary>买菜清单。</summary>
public sealed record ShoppingListDto
{
    /// <summary>合并后的食材。</summary>
    public IReadOnlyList<ShoppingItemDto> Items { get; init; } = [];

    /// <summary>按分类分组的文本摘要，供一键复制。</summary>
    public string TextSummary { get; init; } = string.Empty;

    /// <summary>是否由桩模型生成。</summary>
    public bool IsStubModel { get; init; }
}
