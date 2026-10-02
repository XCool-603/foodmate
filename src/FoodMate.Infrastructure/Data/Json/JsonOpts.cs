using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoodMate.Infrastructure.Data.Json;

/// <summary>JSON 序列化配置。所有 JSON 列共用。</summary>
internal static class JsonOpts
{
    /// <summary>
    /// 默认选项。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> 让中文按原样存储，
    /// 而不是 <c>\uXXXX</c> 转义——数据库里的 JSON 列需要人眼可读，
    /// 这对 AI 修正数据（数据资产）的人工审查尤其重要。
    /// </remarks>
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };
}
