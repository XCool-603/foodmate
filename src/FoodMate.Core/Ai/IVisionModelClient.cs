namespace FoodMate.Core.Ai;

/// <summary>视觉模型识别出的一道菜。</summary>
/// <param name="Name">菜名（模型给出的原始文本）。</param>
/// <param name="Confidence">置信度 0–1。</param>
/// <param name="EstimatedCalories">估算热量（kcal）。</param>
/// <param name="Ingredients">模型认为的主要食材。</param>
/// <param name="Portion">份量估计，1.0 = 一份。</param>
public sealed record RecognizedDishItem(
    string Name,
    double Confidence,
    int? EstimatedCalories = null,
    IReadOnlyList<string>? Ingredients = null,
    double Portion = 1.0)
{
    /// <summary>置信度低于此值时，前端应标黄并引导用户确认。</summary>
    public const double ReviewThreshold = 0.70;

    /// <summary>是否需要用户重点复核。</summary>
    public bool NeedsReview => Confidence < ReviewThreshold;
}

/// <summary>一次视觉识别的完整结果。</summary>
public sealed record VisionRecognitionResult
{
    /// <summary>识别出的菜品。</summary>
    public IReadOnlyList<RecognizedDishItem> Items { get; init; } = [];

    /// <summary>场景：<c>dine_in</c> / <c>takeout</c> / <c>homemade</c> / <c>unknown</c>。</summary>
    public string Scene { get; init; } = "unknown";

    /// <summary>整体置信度。</summary>
    public double OverallConfidence { get; init; }

    /// <summary>模型原始返回，用于诊断。</summary>
    public string? RawResponse { get; init; }

    /// <summary>提示词 token 数。</summary>
    public int? PromptTokens { get; init; }

    /// <summary>补全 token 数。</summary>
    public int? CompletionTokens { get; init; }

    /// <summary>模型耗时（毫秒）。</summary>
    public long LatencyMs { get; init; }

    /// <summary>未识别出任何菜品。</summary>
    public static VisionRecognitionResult Empty(string? raw = null) => new()
    {
        Items = [],
        RawResponse = raw,
    };
}

/// <summary>AI 调用失败。</summary>
public sealed class AiClientException : Exception
{
    public AiClientException(string message, bool isTimeout = false, Exception? inner = null)
        : base(message, inner)
        => IsTimeout = isTimeout;

    /// <summary>是否因超时失败（前端提示不同）。</summary>
    public bool IsTimeout { get; }
}

/// <summary>
/// 菜品图像识别客户端。
/// </summary>
/// <remarks>
/// <para>
/// 抽象成接口有两个目的：<b>①</b> 换模型供应商不改业务代码；
/// <b>②</b> 测试与本地开发可以注入确定性的桩实现，不依赖任何外部服务与密钥。
/// </para>
/// <para>
/// ⚠️ <b>AI 是增强而非依赖。</b>任何实现都必须允许失败，
/// 上层会降级为手动输入——绝不能因为模型不可用就让用户无法记录。
/// </para>
/// </remarks>
public interface IVisionModelClient
{
    /// <summary>模型名称，写入 <c>ai_recognition_logs.model_name</c>。</summary>
    string ModelName { get; }

    /// <summary>是否为桩实现（用于在 API 响应中如实标注，避免误把假数据当真）。</summary>
    bool IsStub { get; }

    /// <summary>识别图片中的菜品。</summary>
    /// <exception cref="AiClientException">模型不可用、超时或返回无法解析。</exception>
    Task<VisionRecognitionResult> RecognizeDishesAsync(
        string imageUrl,
        CancellationToken ct = default);
}
