namespace FoodMate.Core;

/// <summary>
/// 快捷需求标签。用户在决策页勾选，参与 <c>S_mood</c> 打分。
/// </summary>
/// <remarks>
/// 这些标签必须与决策引擎的 <c>MoodMatchers</c> 注册表保持一一对应，
/// 否则用户勾选的标签不会影响结果。
/// </remarks>
public static class MoodTags
{
    /// <summary>想吃辣的。</summary>
    public const string Spicy = "辣的";

    /// <summary>想吃清淡的。</summary>
    public const string Light = "清淡的";

    /// <summary>想吃暖胃的。</summary>
    public const string Warming = "暖胃的";

    /// <summary>想快点吃上。</summary>
    public const string Quick = "快手";

    /// <summary>想下饭的。</summary>
    public const string RiceFriendly = "下饭";

    /// <summary>想喝汤汤水水。</summary>
    public const string Soupy = "汤汤水水";

    /// <summary>想解腻。</summary>
    public const string Refreshing = "解腻";

    /// <summary>全部标签。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Spicy, Light, Warming, Quick, RiceFriendly, Soupy, Refreshing,
    ];
}

/// <summary>常见过敏原。用于忌口匹配的兜底校验。</summary>
public static class CommonAllergens
{
    /// <summary>花生。</summary>
    public const string Peanut = "花生";

    /// <summary>坚果。</summary>
    public const string TreeNut = "坚果";

    /// <summary>海鲜。</summary>
    public const string Seafood = "海鲜";

    /// <summary>乳制品。</summary>
    public const string Dairy = "乳制品";

    /// <summary>麸质。</summary>
    public const string Gluten = "麸质";

    /// <summary>蛋类。</summary>
    public const string Egg = "蛋";

    /// <summary>大豆。</summary>
    public const string Soy = "大豆";

    /// <summary>全部常见过敏原。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Peanut, TreeNut, Seafood, Dairy, Gluten, Egg, Soy,
    ];
}
