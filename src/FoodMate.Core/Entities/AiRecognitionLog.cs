using FoodMate.Core.Enums;

namespace FoodMate.Core.Entities;

/// <summary>
/// AI 识别日志。既是监控数据，也是<b>数据资产</b>。
/// </summary>
/// <remarks>
/// <see cref="CorrectedResult"/> 记录了「模型识别成什么 → 用户改成什么」，
/// 是优化 Prompt、构建 few-shot 示例库、微调模型的黄金数据。
/// </remarks>
public sealed class AiRecognitionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public AiLogStatus Status { get; set; }

    /// <summary>模型原始返回（JSON 字符串），用于诊断。</summary>
    public string? RawResponse { get; set; }

    /// <summary>解析后的结构化结果（JSON 字符串）。</summary>
    public string? ParsedResult { get; set; }

    /// <summary>用户修正后的结果（JSON 字符串）。核心数据资产。</summary>
    public string? CorrectedResult { get; set; }

    public bool IsCorrected { get; set; }

    public string? ErrorMessage { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int LatencyMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // ── 导航属性 ────────────────────────────────────────
    public User? User { get; set; }
}
