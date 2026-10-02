using FoodMate.Contracts.Decisions;
using FoodMate.Contracts.Meta;
using FoodMate.Core;
using FoodMate.Core.Decision;
using FoodMate.Core.Enums;
using Microsoft.Extensions.Options;

namespace FoodMate.Api.Endpoints;

/// <summary>元信息端点：枚举字典与服务信息。</summary>
public static class MetaEndpoints
{
    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/meta/enums", () => new EnumDictionaryDto
        {
            Platform = EnumOptionDto.From<Platform>(EnumLabels.Platform),
            MealType = EnumOptionDto.From<MealType>(EnumLabels.MealType),
            DiningMode = EnumOptionDto.From<DiningMode>(EnumLabels.DiningMode),
            Cuisine = EnumOptionDto.From<Cuisine>(EnumLabels.Cuisine),
            DishCategory = EnumOptionDto.From<DishCategory>(EnumLabels.DishCategory),
            SpicyLevel = EnumOptionDto.Range(0, 5, EnumLabels.SpicyLevel),
            RecordSource = EnumOptionDto.From<RecordSource>(EnumLabels.RecordSource),
            MoodTag = [.. MoodTags.All],
            AvoidIngredient = [.. CommonAllergens.All],
        })
        .WithName("GetEnumDictionary")
        .WithSummary("获取枚举字典")
        .WithTags("元信息");

        app.MapGet("/meta/info", (IHostEnvironment env) => new ServiceMetaDto
        {
            Name = "美食伴侣",
            Version = AppInfo.Version,
            Environment = env.EnvironmentName,
            ServerTime = DateTimeOffset.UtcNow,
        })
        .WithName("GetServiceMeta")
        .WithSummary("获取服务元信息")
        .WithTags("元信息");

        app.MapGet("/meta/engine", (IOptions<EngineOptions> options) =>
        {
            var engine = options.Value;

            return new EngineMetaDto
            {
                Version = engine.Version,
                Weights = Enum.GetValues<ScoreDimension>().ToDictionary(
                    ScoreDimensionKeys.Key,
                    dimension => engine.Weights[dimension]),
                DimensionLabels = Enum.GetValues<ScoreDimension>().ToDictionary(
                    ScoreDimensionKeys.Key,
                    ScoreDimensionKeys.Label),
            };
        })
        .WithName("GetEngineMeta")
        .WithSummary("获取决策引擎权重")
        .WithDescription("前端用这些权重把 breakdown 的归一化分换算成贡献度条形图。")
        .WithTags("元信息");

        return app;
    }
}
