namespace FoodMate.Contracts.Dishes;

/// <summary>菜品食材。</summary>
/// <param name="Name">食材名。</param>
/// <param name="Amount">用量。</param>
/// <param name="IsCommonAllergen">是否常见过敏原。</param>
public sealed record DishIngredientDto(string Name, string? Amount, bool IsCommonAllergen);

/// <summary>菜谱步骤。</summary>
/// <param name="Order">序号。</param>
/// <param name="Text">步骤描述。</param>
/// <param name="DurationMinutes">耗时（分钟）。</param>
public sealed record DishRecipeStepDto(int Order, string Text, int? DurationMinutes);

/// <summary>菜谱。</summary>
public sealed record DishRecipeDto
{
    /// <summary>份数。</summary>
    public short Servings { get; init; }

    /// <summary>总时长（分钟）。</summary>
    public short CookMinutes { get; init; }

    /// <summary>难度 1–3。</summary>
    public short Difficulty { get; init; }

    /// <summary>难度名称。</summary>
    public string DifficultyLabel { get; init; } = string.Empty;

    /// <summary>步骤。</summary>
    public IReadOnlyList<DishRecipeStepDto> Steps { get; init; } = [];

    /// <summary>小贴士。</summary>
    public string? Tips { get; init; }
}

/// <summary>
/// 菜品详情。
/// </summary>
/// <remarks>
/// 比 <see cref="DishBriefDto"/> 多出食材与菜谱 ——
/// 「自己做」模式下用户需要知道<b>用什么、怎么做</b>，光有菜名没有意义。
/// </remarks>
public sealed record DishDetailDto
{
    /// <summary>菜品 ID。</summary>
    public Guid Id { get; init; }

    /// <summary>菜名。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>别名。</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>菜系。</summary>
    public short Cuisine { get; init; }

    /// <summary>菜系名称。</summary>
    public string CuisineLabel { get; init; } = string.Empty;

    /// <summary>分类。</summary>
    public short Category { get; init; }

    /// <summary>分类名称。</summary>
    public string CategoryLabel { get; init; } = string.Empty;

    /// <summary>辣度。</summary>
    public short SpicyLevel { get; init; }

    /// <summary>辣度名称。</summary>
    public string SpicyLabel { get; init; } = string.Empty;

    /// <summary>参考价下限（分）。</summary>
    public int? PriceMinCents { get; init; }

    /// <summary>参考价上限（分）。</summary>
    public int? PriceMaxCents { get; init; }

    /// <summary>每份热量（kcal）。</summary>
    public int? Calories { get; init; }

    /// <summary>图片。</summary>
    public string? ImageUrl { get; init; }

    /// <summary>一句话描述。</summary>
    public string? Description { get; init; }

    /// <summary>标签。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>适用餐次名称。</summary>
    public IReadOnlyList<string> MealTimeLabels { get; init; } = [];

    /// <summary>食材。</summary>
    public IReadOnlyList<DishIngredientDto> Ingredients { get; init; } = [];

    /// <summary>菜谱；菜品库中尚未缓存时为 null，前端可调 AI 生成。</summary>
    public DishRecipeDto? Recipe { get; init; }

    /// <summary>是否内置菜品。</summary>
    public bool IsBuiltin { get; init; }

    /// <summary>与用户忌口冲突的食材。</summary>
    public IReadOnlyList<string> ConflictIngredients { get; init; } = [];
}
