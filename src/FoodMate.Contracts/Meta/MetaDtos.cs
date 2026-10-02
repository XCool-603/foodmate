namespace FoodMate.Contracts.Meta;

/// <summary>枚举字典。前端启动时拉取并缓存，避免硬编码枚举值。</summary>
public sealed record EnumDictionaryDto
{
    public IReadOnlyList<EnumOptionDto> Platform { get; init; } = [];

    public IReadOnlyList<EnumOptionDto> MealType { get; init; } = [];

    public IReadOnlyList<EnumOptionDto> DiningMode { get; init; } = [];

    public IReadOnlyList<EnumOptionDto> Cuisine { get; init; } = [];

    public IReadOnlyList<EnumOptionDto> DishCategory { get; init; } = [];

    public IReadOnlyList<EnumOptionDto> SpicyLevel { get; init; } = [];

    public IReadOnlyList<EnumOptionDto> RecordSource { get; init; } = [];

    public IReadOnlyList<string> MoodTag { get; init; } = [];

    public IReadOnlyList<string> AvoidIngredient { get; init; } = [];
}

/// <summary>枚举选项。</summary>
/// <param name="Value">枚举数值。</param>
/// <param name="Label">中文显示名。</param>
public sealed record EnumOptionDto(short Value, string Label)
{
    /// <summary>把枚举的全部成员展开为选项列表，供前端渲染选择器。</summary>
    public static IReadOnlyList<EnumOptionDto> From<TEnum>(Func<TEnum, string> label)
        where TEnum : struct, Enum
        => [.. Enum.GetValues<TEnum>().Select(v => new EnumOptionDto(Convert.ToInt16(v), label(v)))];

    /// <summary>把连续整数区间展开为选项列表（用于辣度这类非枚举的刻度）。</summary>
    public static IReadOnlyList<EnumOptionDto> Range(int fromInclusive, int toInclusive, Func<short, string> label)
        => [.. Enumerable.Range(fromInclusive, toInclusive - fromInclusive + 1)
            .Select(i => new EnumOptionDto((short)i, label((short)i)))];
}

/// <summary>服务元信息。</summary>
public sealed record ServiceMetaDto
{
    public string Name { get; init; } = "美食伴侣";

    public string Version { get; init; } = string.Empty;

    public string Environment { get; init; } = string.Empty;

    public DateTimeOffset ServerTime { get; init; }
}
