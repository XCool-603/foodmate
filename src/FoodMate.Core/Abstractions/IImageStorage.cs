namespace FoodMate.Core.Abstractions;

/// <summary>图片存储结果。</summary>
/// <param name="Url">可访问的图片地址。</param>
/// <param name="Width">宽度（未知为 null）。</param>
/// <param name="Height">高度（未知为 null）。</param>
/// <param name="SizeBytes">文件大小。</param>
public sealed record StoredImage(string Url, int? Width, int? Height, long SizeBytes);

/// <summary>上传的图片。</summary>
/// <param name="Content">内容流。</param>
/// <param name="FileName">原始文件名。</param>
/// <param name="ContentType">MIME 类型。</param>
/// <param name="SizeBytes">字节数。</param>
public sealed record ImageUpload(Stream Content, string FileName, string ContentType, long SizeBytes);

/// <summary>
/// 图片存储。
/// </summary>
/// <remarks>
/// v1 提供本地磁盘实现，够用且零成本。
/// 上线后换成对象存储（COS/OSS）只需新增一个实现，业务代码不动。
/// </remarks>
public interface IImageStorage
{
    /// <summary>保存图片并返回<b>相对</b>访问路径。</summary>
    /// <param name="userId">归属用户，用于分目录与配额统计。</param>
    /// <param name="upload">图片内容。</param>
    /// <param name="ct">取消令牌。</param>
    /// <remarks>
    /// 返回相对路径（如 <c>/uploads/{userId}/202506/xxx.jpg</c>）而非绝对地址，
    /// 这样同一份数据在开发机、内网、生产域名下都能用，
    /// 前端拿 API 基地址拼一下即可。
    /// </remarks>
    Task<StoredImage> SaveAsync(Guid userId, ImageUpload upload, CancellationToken ct = default);

    /// <summary>
    /// 把相对路径解析为<b>模型可直接消费</b>的地址。
    /// </summary>
    /// <remarks>
    /// 视觉模型需要一个它能访问到的图片地址。本地开发时文件并不对外可达，
    /// 因此这里把本地文件读成 base64 data URL 内联传给模型；
    /// 若传入的本就是 http(s) 绝对地址，则原样返回。
    /// </remarks>
    Task<string> ResolveForModelAsync(string url, CancellationToken ct = default);
}

/// <summary>图片校验或存储失败。</summary>
public sealed class ImageStorageException(string message, Exception? inner = null)
    : Exception(message, inner);
