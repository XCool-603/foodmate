using FoodMate.Core.Decision;
using FoodMate.Core.Decision.Scoring;
using FoodMate.Core.Entities;
using FoodMate.Core.Enums;

namespace FoodMate.Core.Tests.Decision;

/// <summary>测试数据构造器。</summary>
internal static class Build
{
    public static readonly DateTimeOffset Now =
        new(2025, 6, 15, 4, 0, 0, TimeSpan.Zero);   // 北京时间 12:00（午餐）

    public static DishCandidate Dish(
        string name = "测试菜",
        Guid? id = null,
        Cuisine cuisine = Cuisine.Other,
        DishCategory category = DishCategory.Meat,
        short spicy = 0,
        int? priceMin = 2000,
        int? priceMax = 3000,
        int? calories = 300,
        Ingredient[]? ingredients = null,
        string[]? tags = null,
        MealTimeMask mealTimes = MealTimeMask.All,
        SeasonMask seasons = SeasonMask.AllYear,
        bool hasRecipe = false,
        bool canMakeAtHome = true,
        short? cookMinutes = null,
        int popularity = 50,
        double? distanceKm = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Cuisine = cuisine,
            Category = category,
            SpicyLevel = spicy,
            PriceMinCents = priceMin,
            PriceMaxCents = priceMax,
            Calories = calories,
            Ingredients = ingredients ?? [],
            Tags = tags ?? [],
            MealTimes = mealTimes,
            Seasons = seasons,
            HasRecipe = hasRecipe,
            CanMakeAtHome = canMakeAtHome,
            CookMinutes = cookMinutes,
            Popularity = popularity,
            DistanceKm = distanceKm,
        };

    public static DecisionRequest Request(
        MealType? mealType = MealType.Lunch,
        DiningMode diningMode = DiningMode.Whatever,
        short partySize = 1,
        int? budgetMin = null,
        int? budgetMax = null,
        GeoPoint? location = null,
        string? weather = null,
        string[]? moodTags = null,
        Guid[]? excludeDishIds = null,
        double exploreJitter = 0)
        => new()
        {
            MealType = mealType,
            DiningMode = diningMode,
            PartySize = partySize,
            BudgetMinCents = budgetMin,
            BudgetMaxCents = budgetMax,
            Location = location,
            Weather = weather,
            MoodTags = moodTags ?? [],
            ExcludeDishIds = excludeDishIds ?? [],
            ExploreJitter = exploreJitter,
            Now = Now,
            LocalOffset = TimeSpan.FromHours(8),
        };

    public static DecisionContext Context(
        IReadOnlyList<DishCandidate> candidates,
        UserPreferenceSnapshot? preference = null,
        DecisionRequest? request = null,
        Dictionary<Guid, DishStatSnapshot>? dishStats = null,
        Dictionary<Cuisine, CuisineStatSnapshot>? cuisineStats = null,
        int userRecordCount = 10,
        EngineOptions? options = null)
        => new()
        {
            Request = request ?? Request(),
            Preference = preference ?? UserPreferenceSnapshot.Default,
            Candidates = candidates,
            DishStats = dishStats ?? [],
            CuisineStats = cuisineStats ?? [],
            UserRecordCount = userRecordCount,
            Options = options ?? new EngineOptions(),
        };

    public static DecisionEngine Engine(IRandomSource? random = null)
        => new(
            scorers:
            [
                new TasteScorer(),
                new FreshnessScorer(),
                new AffinityScorer(),
                new TimeSlotScorer(),
                new BudgetScorer(),
                new ContextScorer(),
                new ExplorationScorer(),
            ],
            penalties: new PenaltyEvaluator(),
            reasons: new ReasonGenerator(),
            random: random ?? DeterministicRandomSource.Instance);
}
