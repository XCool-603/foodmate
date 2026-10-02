using System.Reflection;

namespace FoodMate.Api;

/// <summary>应用元信息。</summary>
internal static class AppInfo
{
    /// <summary>服务版本号，取自程序集信息版本（去掉源码版本后缀）。</summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(AppInfo).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // 形如 "0.1.0+1a2b3c4" → 取 "0.1.0"
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
