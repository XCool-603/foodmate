using FoodMate.Core.Enums;

namespace FoodMate.Core;

/// <summary>
/// 枚举的中文显示名。集中定义，供 API 响应与推荐理由文案复用，
/// 避免中文散落在各处。
/// </summary>
public static class EnumLabels
{
    /// <summary>平台名称。</summary>
    public static string Platform(Enums.Platform value) => value switch
    {
        Enums.Platform.WeChat => "微信",
        Enums.Platform.Alipay => "支付宝",
        Enums.Platform.Douyin => "抖音",
        Enums.Platform.H5 => "网页",
        _ => "未知",
    };

    /// <summary>餐次名称。</summary>
    public static string MealType(Enums.MealType value) => value switch
    {
        Enums.MealType.Breakfast => "早餐",
        Enums.MealType.Lunch => "午餐",
        Enums.MealType.Dinner => "晚餐",
        Enums.MealType.LateNight => "夜宵",
        _ => "这一餐",
    };

    /// <summary>就餐方式名称。</summary>
    public static string DiningMode(Enums.DiningMode value) => value switch
    {
        Enums.DiningMode.Whatever => "随便",
        Enums.DiningMode.Takeout => "外卖",
        Enums.DiningMode.DineIn => "堂食",
        Enums.DiningMode.Homemade => "自己做",
        _ => "未知",
    };

    /// <summary>菜系名称。</summary>
    public static string Cuisine(Enums.Cuisine value) => value switch
    {
        Enums.Cuisine.Other => "家常",
        Enums.Cuisine.Sichuan => "川菜",
        Enums.Cuisine.Cantonese => "粤菜",
        Enums.Cuisine.Hunan => "湘菜",
        Enums.Cuisine.Shandong => "鲁菜",
        Enums.Cuisine.Jiangsu => "苏菜",
        Enums.Cuisine.Zhejiang => "浙菜",
        Enums.Cuisine.Fujian => "闽菜",
        Enums.Cuisine.Anhui => "徽菜",
        Enums.Cuisine.Northeast => "东北菜",
        Enums.Cuisine.Northwest => "西北菜",
        Enums.Cuisine.YunnanGuizhou => "云贵菜",
        Enums.Cuisine.Japanese => "日料",
        Enums.Cuisine.Korean => "韩餐",
        Enums.Cuisine.Western => "西餐",
        Enums.Cuisine.SoutheastAsian => "东南亚",
        Enums.Cuisine.Snack => "快餐小吃",
        _ => "其他",
    };

    /// <summary>菜品分类名称。</summary>
    public static string DishCategory(Enums.DishCategory value) => value switch
    {
        Enums.DishCategory.Unknown => "未分类",
        Enums.DishCategory.Staple => "主食",
        Enums.DishCategory.Meat => "荤菜",
        Enums.DishCategory.Vegetable => "素菜",
        Enums.DishCategory.Soup => "汤羹",
        Enums.DishCategory.Snack => "小吃",
        Enums.DishCategory.Dessert => "甜点",
        Enums.DishCategory.Drink => "饮品",
        Enums.DishCategory.Breakfast => "早餐",
        Enums.DishCategory.Hotpot => "火锅烧烤",
        _ => "其他",
    };

    /// <summary>辣度名称。</summary>
    public static string SpicyLevel(short level) => level switch
    {
        <= 0 => "不辣",
        1 => "微辣",
        2 => "小辣",
        3 => "中辣",
        4 => "重辣",
        _ => "变态辣",
    };

    /// <summary>记录来源名称。</summary>
    public static string RecordSource(Enums.RecordSource value) => value switch
    {
        Enums.RecordSource.Manual => "手动",
        Enums.RecordSource.AiRecognized => "AI 识别",
        Enums.RecordSource.Decision => "决策推荐",
        Enums.RecordSource.DishLibrary => "菜品库",
        _ => "未知",
    };

    /// <summary>菜谱难度名称。</summary>
    public static string Difficulty(short value) => value switch
    {
        <= 1 => "简单",
        2 => "中等",
        _ => "复杂",
    };
}
