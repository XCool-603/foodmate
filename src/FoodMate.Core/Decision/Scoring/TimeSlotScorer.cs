using FoodMate.Core.Enums;

namespace FoodMate.Core.Decision.Scoring;

/// <summary>
/// 时段契合（<c>S_timeSlot</c>，默认权重 0.15）。
/// </summary>
/// <remarks>
/// <code>
/// S_mealTime = 适用当前餐次 ? 1.00 : 0.10
/// S_season   = 四季皆宜 或 适用当前季节 ? 1.00 : 0.65
/// S_timeSlot = 0.8·S_mealTime + 0.2·S_season
/// </code>
/// 这条维度负责最基本的常识：早上不推红烧肉。
/// </remarks>
public sealed class TimeSlotScorer : IScorer
{
    /// <inheritdoc />
    public ScoreDimension Dimension => ScoreDimension.TimeSlot;

    /// <inheritdoc />
    public double Score(DishCandidate dish, DecisionContext context)
    {
        var mealType = context.Request.ResolveMealType();
        var mask = MealTimes.ToMask(mealType);

        var mealTimeScore = dish.MealTimes.HasFlag(mask) ? 1.00 : 0.10;

        var seasonScore =
            dish.Seasons == SeasonMask.AllYear || dish.Seasons.HasFlag(context.Request.CurrentSeason)
                ? 1.00
                : 0.65;

        return 0.8 * mealTimeScore + 0.2 * seasonScore;
    }
}
