namespace FoodMate.Core.Ai;

/// <summary>菜谱步骤。</summary>
/// <param name="Order">序号，从 1 开始。</param>
/// <param name="Text">步骤描述。</param>
/// <param name="DurationMinutes">该步骤耗时（分钟）。</param>
public sealed record GeneratedRecipeStep(int Order, string Text, int? DurationMinutes = null);

/// <summary>生成的菜谱。</summary>
public sealed record GeneratedRecipe
{
    /// <summary>菜名。</summary>
    public string DishName { get; init; } = string.Empty;

    /// <summary>份数。</summary>
    public short Servings { get; init; } = 2;

    /// <summary>总时长（分钟）。</summary>
    public short CookMinutes { get; init; }

    /// <summary>难度 1–3。</summary>
    public short Difficulty { get; init; } = 1;

    /// <summary>步骤。</summary>
    public IReadOnlyList<GeneratedRecipeStep> Steps { get; init; } = [];

    /// <summary>小贴士。</summary>
    public string? Tips { get; init; }
}

/// <summary>买菜清单中的一项。</summary>
/// <param name="Name">食材名。</param>
/// <param name="TotalAmount">合计用量。</param>
/// <param name="Category">分类，如「蔬菜」「肉类」。</param>
/// <param name="ForDishes">用于哪些菜。</param>
public sealed record ShoppingListItem(
    string Name,
    string? TotalAmount,
    string? Category,
    IReadOnlyList<string> ForDishes);

/// <summary>买菜清单。</summary>
public sealed record ShoppingList
{
    /// <summary>合并后的食材项。</summary>
    public IReadOnlyList<ShoppingListItem> Items { get; init; } = [];

    /// <summary>按分类分组的文本摘要，供前端「一键复制」。</summary>
    public string TextSummary { get; init; } = string.Empty;
}

/// <summary>
/// 文本生成客户端（菜谱、买菜清单、推荐理由润色）。
/// </summary>
public interface ITextModelClient
{
    /// <summary>模型名称。</summary>
    string ModelName { get; }

    /// <summary>是否为桩实现。</summary>
    bool IsStub { get; }

    /// <summary>生成菜谱。</summary>
    /// <exception cref="AiClientException">模型不可用或返回无法解析。</exception>
    Task<GeneratedRecipe> GenerateRecipeAsync(
        string dishName,
        short servings,
        CancellationToken ct = default);

    /// <summary>根据多道菜生成买菜清单。</summary>
    /// <exception cref="AiClientException">模型不可用或返回无法解析。</exception>
    Task<ShoppingList> GenerateShoppingListAsync(
        IReadOnlyList<string> dishNames,
        short servings,
        CancellationToken ct = default);
}
