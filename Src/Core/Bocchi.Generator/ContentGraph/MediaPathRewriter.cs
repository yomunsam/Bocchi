using System.Globalization;
using System.Text.RegularExpressions;

using Bocchi.ContentModel;
using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Utilities;

namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 把"相对源文件"的媒体路径改写为"站点根相对"路径，并产出 <see cref="MediaAsset"/> 清单。
/// 详见 <c>Docs/Milestones/M3/M3.md §3.4</c>。
/// </summary>
/// <remarks>
/// 改写规则：
/// <list type="bullet">
///   <item><description>Post / Work：媒体源 = <c>&lt;ownerDir&gt;/&lt;rel&gt;</c>；站点路径 = <c>/media/{kind}/{year}/{slug}/{fileName}</c>。</description></item>
///   <item><description>Page：媒体源 = <c>&lt;ownerDir&gt;/&lt;rel&gt;</c>；站点路径 = <c>/media/pages/{slug}/{fileName}</c>。</description></item>
///   <item><description>Note：媒体源 = <c>&lt;notes/year&gt;/&lt;rel&gt;</c>；站点路径 = <c>/media/notes/{year}/{fileName}</c>。</description></item>
///   <item><description>Friend：友链头像位于内容 workspace 的 <c>friends/assets/...</c>；站点路径 = <c>/media/friends/{fileName}</c>。</description></item>
/// </list>
/// 已是 <c>http(s)://</c>、协议相对、绝对站点路径（<c>/...</c>）或 <c>data:</c> 的引用不再改写。
/// </remarks>
public sealed partial class MediaPathRewriter
{
    private readonly Dictionary<string, MediaAsset> _assetsBySource = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前累积的去重媒体资源。</summary>
    public IReadOnlyList<MediaAsset> Assets => _assetsBySource.Values
        .OrderBy(a => a.SiteRelativePath, StringComparer.Ordinal)
        .ToList();

    /// <summary>把 Markdown 正文中所有图片引用就地改写为站点路径，并登记 <see cref="MediaAsset"/>。</summary>
    /// <param name="markdown">原始 Markdown。</param>
    /// <param name="ownerDirAbsolute">源 Markdown 文件所在目录的绝对路径。</param>
    /// <param name="ownerSiteMediaPrefix">该 owner 媒体的站点路径前缀，例如 <c>/media/posts/2026/hello</c>，**不含尾部 <c>/</c>**。</param>
    /// <param name="ownerDescriptor">便于日志的 owner 描述，例如 <c>posts/2026/hello</c>。</param>
    /// <returns>改写后的 Markdown。</returns>
    public string RewriteMarkdown(string markdown, string ownerDirAbsolute, string ownerSiteMediaPrefix, string ownerDescriptor)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDirAbsolute);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSiteMediaPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDescriptor);

        return MarkdownImageRegex().Replace(markdown, match =>
        {
            var alt = match.Groups["alt"].Value;
            var url = match.Groups["url"].Value;
            var title = match.Groups["title"].Value;
            if (IsExternal(url))
            {
                return match.Value;
            }

            var sitePath = RegisterMedia(url, ownerDirAbsolute, ownerSiteMediaPrefix, ownerDescriptor).SiteRelativePath;
            return string.IsNullOrEmpty(title)
                ? string.Format(CultureInfo.InvariantCulture, "![{0}]({1})", alt, sitePath)
                : string.Format(CultureInfo.InvariantCulture, "![{0}]({1} {2})", alt, sitePath, title);
        });
    }

    /// <summary>登记一个 frontmatter 字段中出现的媒体引用（封面、头像等），返回站点路径。</summary>
    /// <param name="originalPath">frontmatter 中"相对源文件"的路径，或外部 URL。</param>
    /// <param name="ownerDirAbsolute">owner 目录的绝对路径。</param>
    /// <param name="ownerSiteMediaPrefix">站点路径前缀（不含尾部 <c>/</c>）。</param>
    /// <param name="ownerDescriptor">日志用。</param>
    /// <returns>已改写的 <see cref="MediaReference"/>；外部链接原样返回。</returns>
    public MediaReference RewriteReference(MediaReference original, string ownerDirAbsolute, string ownerSiteMediaPrefix, string ownerDescriptor)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (IsExternal(original.Path))
        {
            return original;
        }

        var asset = RegisterMedia(original.Path, ownerDirAbsolute, ownerSiteMediaPrefix, ownerDescriptor);
        return new MediaReference(asset.SiteRelativePath, original.Alt);
    }

    /// <summary>给定相对源路径，计算或返回缓存的 <see cref="MediaAsset"/>。</summary>
    public MediaAsset RegisterMedia(string relativeOrSitePath, string ownerDirAbsolute, string ownerSiteMediaPrefix, string ownerDescriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeOrSitePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDirAbsolute);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSiteMediaPrefix);

        if (relativeOrSitePath.StartsWith('/'))
        {
            // 已经是站点根相对：当成"主题外部已确认"路径，不重新登记 asset
            // 但当前 owner 仍可能改写它（M3 不支持站点根相对路径作为内容写法；保留行为）
            throw new ContentGraphException(
                $"内容 '{ownerDescriptor}' 引用了站点绝对路径 '{relativeOrSitePath}'；M3 仅接受相对源文件的路径或 http(s) 外部链接。");
        }

        var normalizedRel = relativeOrSitePath.Replace('\\', '/');
        var sourceAbs = Path.GetFullPath(Path.Combine(ownerDirAbsolute, normalizedRel));
        if (!File.Exists(sourceAbs))
        {
            throw new ContentGraphException(
                $"内容 '{ownerDescriptor}' 引用了不存在的媒体 '{relativeOrSitePath}' （解析为 '{sourceAbs}'）。");
        }

        if (_assetsBySource.TryGetValue(sourceAbs, out var existing))
        {
            return existing;
        }

        var fileName = Path.GetFileName(sourceAbs);
        var sitePath = string.Format(CultureInfo.InvariantCulture, "{0}/{1}", ownerSiteMediaPrefix.TrimEnd('/'), fileName);
        if (!sitePath.StartsWith('/'))
        {
            sitePath = "/" + sitePath;
        }

        var info = new FileInfo(sourceAbs);
        var sha = Sha256Hex.FromFile(sourceAbs);
        var contentType = ContentTypeMap.GuessFromFileName(fileName);
        var asset = new MediaAsset
        {
            SourceAbsolutePath = sourceAbs,
            SiteRelativePath = sitePath,
            Sha256 = sha,
            ContentType = contentType,
            SizeBytes = info.Length,
        };
        _assetsBySource[sourceAbs] = asset;
        return asset;
    }

    private static bool IsExternal(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        return url.Contains("://", StringComparison.Ordinal)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
    }

    // 匹配 Markdown 行内图片：![alt](url "title")。alt / title 可空，title 可省。
    // 注意：本正则不处理 <img> 标签或引用式图片；M3 内容 workspace 约束为标准 Markdown 写法。
    [GeneratedRegex(
        @"!\[(?<alt>[^\]]*)\]\((?<url>[^\s\)""]+)(?:\s+""(?<title>[^""]*)"")?\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImageRegex();
}
