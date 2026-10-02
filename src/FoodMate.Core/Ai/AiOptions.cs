namespace FoodMate.Core.Ai;

/// <summary>AI 相关配置。</summary>
public sealed class AiOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Ai";

    /// <summary>
    /// Provider：<c>Stub</c>（本地桩，无外部依赖）或 <c>OpenAiCompatible</c>。
    /// </summary>
    public string Provider { get; set; } = "Stub";

    /// <summary>OpenAI 兼容接口的基地址，如 <c>https://dashscope.aliyuncs.com/compatible-mode/v1</c>。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API Key。<b>只从环境变量或用户机密注入，绝不进仓库。</b></summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>视觉模型名，如 <c>qwen-vl-max</c>。</summary>
    public string VisionModel { get; set; } = "qwen-vl-max";

    /// <summary>文本模型名，如 <c>deepseek-chat</c>。</summary>
    public string TextModel { get; set; } = "deepseek-chat";

    /// <summary>单次调用超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>每个用户每小时的识别次数上限。</summary>
    public int RecognizePerHour { get; set; } = 20;

    /// <summary>每个用户每天的识别次数上限。</summary>
    public int RecognizePerDay { get; set; } = 100;

    /// <summary>每个用户每天的菜谱生成次数上限。</summary>
    public int RecipePerDay { get; set; } = 30;

    /// <summary>图片大小上限（字节）。</summary>
    public long MaxImageBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>允许的图片扩展名。</summary>
    public IReadOnlyList<string> AllowedImageExtensions { get; set; } =
        [".jpg", ".jpeg", ".png", ".webp"];
}
