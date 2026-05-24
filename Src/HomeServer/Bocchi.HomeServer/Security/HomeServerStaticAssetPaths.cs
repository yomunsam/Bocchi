namespace Bocchi.HomeServer.Security;

/// <summary>
/// Home Server 自身静态资源路径识别。
/// 这些路径由 ASP.NET Core 静态资源系统负责，不能落入前台 preview catch-all，
/// 否则过期 hash 会拿到 HTML 页面并被浏览器当作 JS/manifest/JSON 解析。
/// </summary>
internal static class HomeServerStaticAssetPaths
{
    /// <summary>判断请求路径是否属于后台壳层和 Blazor 运行时所需的静态资源。</summary>
    public static bool IsHomeServerAsset(PathString path)
    {
        var value = path.Value ?? "/";
        return IsFingerprintedAsset(value, "/app", ".css")
            || IsFingerprintedAsset(value, "/Bocchi.HomeServer", ".styles.css")
            || IsFingerprintedAsset(value, "/favicon", ".ico")
            || IsFingerprintedAsset(value, "/favicon-16x16", ".png")
            || IsFingerprintedAsset(value, "/favicon-32x32", ".png")
            || IsFingerprintedAsset(value, "/favicon-48x48", ".png")
            || IsFingerprintedAsset(value, "/apple-touch-icon", ".png")
            || IsFingerprintedAsset(value, "/site", ".webmanifest")
            || IsFingerprintedAsset(value, "/bocchi-appearance", ".js")
            || IsFingerprintedAsset(value, "/bocchi-ai", ".js")
            || IsFingerprintedAsset(value, "/bocchi-markdown-editor", ".js")
            || value.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/icons/", StringComparison.OrdinalIgnoreCase)
            || (value.StartsWith("/Components/", StringComparison.OrdinalIgnoreCase)
                && value.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>匹配未指纹与带 hash 的静态资源文件名。</summary>
    private static bool IsFingerprintedAsset(string value, string prefix, string suffix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
