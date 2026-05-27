using System.Text;
using System.Text.RegularExpressions;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme renderer 的资产复制、页面写入与站点根相对 URL 改写。</summary>
public sealed partial class DefaultStaticTemplateRenderer
{
    /// <summary>需要随静态站点部署位置搬移的 HTML URL 属性。</summary>
    private static readonly Regex InternalUrlAttributeRegex = new(
        @"(?<prefix>\b(?:href|src|poster)\s*=\s*)(?<quote>[""'])(?<url>/(?!/)[^""']*)(?<suffix>\k<quote>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>复制用户可覆盖资产；缺失时回退到内置资产。</summary>
    private static async Task WriteAssetsAsync(DefaultStaticRenderRequest request, CancellationToken cancellationToken)
    {
        var assetOutput = Path.Combine(request.OutputDirectory, "assets");
        Directory.CreateDirectory(assetOutput);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "favicon.svg", cancellationToken).ConfigureAwait(false);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "app.css", cancellationToken).ConfigureAwait(false);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "app.js", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>优先复制工作区 Theme asset，保证用户修改 CSS/JS 后构建能生效。</summary>
    private static async Task CopyOrWriteAssetAsync(
        string themeRoot,
        string outputDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        var source = Path.Combine(themeRoot, "assets", fileName);
        var destination = Path.Combine(outputDirectory, fileName);
        if (File.Exists(source))
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return;
        }

        await DefaultStaticThemeResources.CopyToFileAsync($"assets/{fileName}", destination, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>把 Theme URL 转为输出目录内的 index.html 路径。</summary>
    private static string ToOutputPath(string url)
        => string.IsNullOrWhiteSpace(url) || url == "/" ? "index.html" : url.Trim('/').Trim() + "/index.html";

    /// <summary>写入一个相对 Theme 输出目录的 HTML 文件。</summary>
    private static async Task WritePageAsync(string outputDirectory, string relativePath, string html, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var relocatableHtml = RelativizeInternalHtmlUrls(html, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, relocatableHtml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 将渲染后的站点根相对 URL 改写为相对当前 HTML 文件的 URL，让同一份输出可以部署在域名根目录或任意二级路径。
    /// </summary>
    private static string RelativizeInternalHtmlUrls(string html, string outputRelativePath)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);

        var pageDirectory = GetPageDirectory(outputRelativePath);
        return InternalUrlAttributeRegex.Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            return string.Concat(
                match.Groups["prefix"].Value,
                match.Groups["quote"].Value,
                ToPageRelativeUrl(pageDirectory, url),
                match.Groups["suffix"].Value);
        });
    }

    /// <summary>根据输出 HTML 路径推导浏览器解析相对 URL 时所在的页面目录。</summary>
    private static string GetPageDirectory(string outputRelativePath)
    {
        var normalized = outputRelativePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash < 0 ? "/" : "/" + normalized[..(lastSlash + 1)];
    }

    /// <summary>把 <paramref name="targetUrl"/> 从站点根相对 URL 转为相对 <paramref name="pageDirectory"/> 的 URL。</summary>
    private static string ToPageRelativeUrl(string pageDirectory, string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl) || targetUrl[0] != '/' || targetUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return targetUrl;
        }

        var suffixStart = targetUrl.IndexOfAny(['?', '#']);
        var targetPath = suffixStart < 0 ? targetUrl : targetUrl[..suffixStart];
        var suffix = suffixStart < 0 ? string.Empty : targetUrl[suffixStart..];
        var currentSegments = SplitUrlPath(pageDirectory);
        var targetSegments = SplitUrlPath(targetPath);
        var common = 0;
        while (common < currentSegments.Length &&
               common < targetSegments.Length &&
               string.Equals(currentSegments[common], targetSegments[common], StringComparison.Ordinal))
        {
            common++;
        }

        var parts = Enumerable.Repeat("..", currentSegments.Length - common)
            .Concat(targetSegments.Skip(common))
            .ToArray();
        var relative = parts.Length == 0 ? "." : string.Join("/", parts);
        if (targetPath.EndsWith('/'))
        {
            relative += "/";
        }

        return relative + suffix;
    }

    /// <summary>拆分 URL path；根路径会返回空数组，便于计算相对层级。</summary>
    private static string[] SplitUrlPath(string path)
        => path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}
