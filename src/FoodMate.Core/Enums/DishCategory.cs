namespace FoodMate.Core.Enums;

/// <summary>菜品分类。</summary>
public enum DishCategory : short
{
    /// <summary>未分类。用户自定义或 AI 识别新建的菜品默认落在这里。</summary>
    Unknown = 0,

    /// <summary>主食（米饭 / 面 / 馒头）</summary>
    Staple = 1,

    /// <summary>荤菜</summary>
    Meat = 2,

    /// <summary>素菜</summary>
    Vegetable = 3,

    /// <summary>汤羹</summary>
    Soup = 4,

    /// <summary>小吃</summary>
    Snack = 5,

    /// <summary>甜点</summary>
    Dessert = 6,

    /// <summary>饮品</summary>
    Drink = 7,

    /// <summary>早餐单品</summary>
    Breakfast = 8,

    /// <summary>火锅烧烤</summary>
    Hotpot = 9,
}
