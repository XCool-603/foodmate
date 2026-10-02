using FoodMate.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace FoodMate.Infrastructure.Storage;

/// <summary>本地磁盘图片存储。</summary>
/// <remarks>
/// <para>
/// 文件落在 <c>{contentRoot}/wwwroot/uploads/{userId}/{yyyyMM}/{guid}.{ext}</c>，
/// 由 <c>UseStaticFiles()</c> 直接对外提供。
/// </para>
/// <para>
/// 分目录按用户 + 月份，既方便配额统计，也避免单目录文件过多。
/// </para>
/// <para>
/// 只依赖一个根路径字符串而非 <c>IHostEnvironment</c>——
/// 少一个依赖，单元测试也不必伪造整个宿主环境。
/// </para>
/// <para>
/// ⚠️ 生产环境应换成对象存储：本地磁盘在容器重启后会丢，
/// 且多实例部署时各实例的文件不共享。
/// </para>
/// </remarks>
public sealed class LocalImageStorage(
    string contentRootPath,
    ILogger<LocalImageStorage> logger) : IImageStorage
{
    private const string UrlPrefix = "/uploads";
    private const string WebRootName = "wwwroot";

    private readonly string _webRoot = Path.GetFullPath(
        Path.Combine(contentRootPath, WebRootName));

    /// <inheritdoc />
    public async Task<StoredImage> SaveAsync(
        Guid userId,
        ImageUpload upload,
        CancellationToken ct = default)
    {
        if (upload.SizeBytes <= 0)
        {
            throw new ImageStorageException("图片内容为空。");
        }

        var extension = NormalizeExtension(upload.FileName, upload.ContentType);

        var now = DateTimeOffset.UtcNow;
        var folder = Path.Combine(_webRoot, "uploads", userId.ToString("N"), now.ToString("yyyyMM"));

        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var target = File.Create(fullPath))
        {
            await upload.Content.CopyToAsync(target, ct);
        }

        var url = $"{UrlPrefix}/{userId:N}/{now:yyyyMM}/{fileName}";

        logger.LogInformation(
            "已保存图片 {Url}（{Size} 字节，用户 {UserId}）", url, upload.SizeBytes, userId);

        return new StoredImage(url, Width: null, Height: null, upload.SizeBytes);
    }

    /// <inheritdoc />
    public async Task<string> ResolveForModelAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ImageStorageException("图片地址为空。");
        }

        // 已经是可公网访问的地址，直接交给模型
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // 本地相对路径 → 读成 base64 data URL 内联传给模型
        var relative = url.TrimStart('/');

        if (!relative.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageStorageException($"不支持的图片路径：{url}");
        }

        var resolved = Path.GetFullPath(Path.Combine(_webRoot, relative));

        // 防目录穿越：确认解析后的路径确实落在 wwwroot 内
        if (!resolved.StartsWith(_webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ImageStorageException("图片路径非法。");
        }

        if (!File.Exists(resolved))
        {
            throw new ImageStorageException($"图片文件不存在：{url}");
        }

        var bytes = await File.ReadAllBytesAsync(resolved, ct);
        var mime = MimeOf(Path.GetExtension(resolved));

        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string NormalizeExtension(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg",
            };
        }

        return extension.ToLowerInvariant();
    }

    private static string MimeOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };
}
