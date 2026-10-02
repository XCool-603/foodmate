namespace FoodMate.Core.Enums;

/// <summary>饮食记录来源。</summary>
public enum RecordSource : short
{
    /// <summary>手动输入</summary>
    Manual = 1,

    /// <summary>AI 拍照识别</summary>
    AiRecognized = 2,

    /// <summary>决策页「就吃这个」</summary>
    Decision = 3,

    /// <summary>菜品库选择</summary>
    DishLibrary = 4,
}
