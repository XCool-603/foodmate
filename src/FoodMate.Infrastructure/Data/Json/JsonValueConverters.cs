using System.Text.Json;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FoodMate.Infrastructure.Data.Json;

/// <summary>
/// JSON 列的值转换器与比较器。
/// </summary>
/// <remarks>
/// <para>
/// 统一把复杂类型序列化为 <c>text</c> 列，以同时兼容 SQLite（开发）与
/// PostgreSQL（生产），不使用任何数据库专有的 JSON 类型（见 ADR-001）。
/// </para>
/// <para>
/// <b>变更追踪注意</b>：转换后的属性使用 JSON 快照比较，
/// 因此原地修改集合（如 <c>dish.Tags.Add("x")</c>）也会被正确检测。
/// 不过仍推荐整体替换集合，语义更清晰。
/// </para>
/// </remarks>
internal static class JsonValueConverters
{
    // ── 转换器 ──────────────────────────────────────────

    public static readonly ValueConverter<List<string>, string> StringList =
        Converter<List<string>>(() => []);

    public static readonly ValueConverter<List<Cuisine>, string> CuisineList =
        Converter<List<Cuisine>>(() => []);

    public static readonly ValueConverter<List<Ingredient>, string> IngredientList =
        Converter<List<Ingredient>>(() => []);

    public static readonly ValueConverter<List<RecipeStep>, string> RecipeStepList =
        Converter<List<RecipeStep>>(() => []);

    public static readonly ValueConverter<List<DecisionCandidateSnapshot>, string> CandidateList =
        Converter<List<DecisionCandidateSnapshot>>(() => []);

    public static readonly ValueConverter<Dictionary<string, double>, string> ScoreDictionary =
        Converter<Dictionary<string, double>>(() => []);

    public static readonly ValueConverter<DishSnapshot, string> DishSnapshotValue =
        Converter<DishSnapshot>(() => new DishSnapshot());

    public static readonly ValueConverter<DiningModeWeights, string> DiningModeWeightsValue =
        Converter<DiningModeWeights>(() => new DiningModeWeights());

    // ── 比较器 ──────────────────────────────────────────

    public static readonly ValueComparer<List<string>> StringListComparer = Comparer<List<string>>();
    public static readonly ValueComparer<List<Cuisine>> CuisineListComparer = Comparer<List<Cuisine>>();
    public static readonly ValueComparer<List<Ingredient>> IngredientListComparer = Comparer<List<Ingredient>>();
    public static readonly ValueComparer<List<RecipeStep>> RecipeStepListComparer = Comparer<List<RecipeStep>>();
    public static readonly ValueComparer<List<DecisionCandidateSnapshot>> CandidateListComparer = Comparer<List<DecisionCandidateSnapshot>>();
    public static readonly ValueComparer<Dictionary<string, double>> ScoreDictionaryComparer = Comparer<Dictionary<string, double>>();
    public static readonly ValueComparer<DishSnapshot> DishSnapshotComparer = Comparer<DishSnapshot>();
    public static readonly ValueComparer<DiningModeWeights> DiningModeWeightsComparer = Comparer<DiningModeWeights>();

    // ── 工厂 ────────────────────────────────────────────

    private static ValueConverter<T, string> Converter<T>(Func<T> fallback)
        where T : class
        => new(
            model => JsonSerializer.Serialize(model, JsonOpts.Default),
            provider => string.IsNullOrWhiteSpace(provider)
                ? fallback()
                : JsonSerializer.Deserialize<T>(provider, JsonOpts.Default) ?? fallback());

    /// <summary>
    /// 基于 JSON 快照的通用比较器：正确、统一，且在本项目的数据规模下开销可忽略。
    /// </summary>
    private static ValueComparer<T> Comparer<T>()
        where T : class
        => new(
            (a, b) => Serialize(a) == Serialize(b),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(Serialize(v), JsonOpts.Default)!);

    private static string Serialize<T>(T? value)
        => value is null ? "null" : JsonSerializer.Serialize(value, JsonOpts.Default);
}
