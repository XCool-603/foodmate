namespace FoodMate.Core.Enums;

/// <summary>AI 识别日志状态。</summary>
public enum AiLogStatus : short
{
    /// <summary>识别并解析成功</summary>
    Success = 1,

    /// <summary>模型返回但 JSON 解析失败</summary>
    ParseFailed = 2,

    /// <summary>模型调用失败</summary>
    ModelFailed = 3,

    /// <summary>用户已确认入库</summary>
    Confirmed = 4,
}
