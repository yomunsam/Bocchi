using System.Globalization;

namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 站点 URL 与媒体路径计算工具。路径方案参见 <c>Docs/Milestones/M3/M3.md §3.4</c>。
/// </summary>
/// <remarks>
/// 所有方法返回的字符串都以 <c>/</c> 开头、以 <c>/</c> 归一化；目录类 URL 以 <c>/</c> 结尾。
/// 不做 URL 编码；内容 slug 由上游规范化为单段 Unicode path segment，浏览器与站点输出层负责最终转义。
/// </remarks>
public static class SiteUrlResolver
{
    /// <summary>文章详情页：<c>/posts/{year}/{slug}/</c>。</summary>
    public static string PostUrl(string year, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(year);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return string.Format(CultureInfo.InvariantCulture, "/posts/{0}/{1}/", year, slug);
    }

    /// <summary>独立页面：<c>/{slug}/</c>，<c>slug</c> == <c>index</c> 时为 <c>/</c>。</summary>
    public static string PageUrl(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return slug.Equals("index", StringComparison.Ordinal)
            ? "/"
            : string.Format(CultureInfo.InvariantCulture, "/{0}/", slug);
    }

    /// <summary>作品详情页：<c>/works/{year}/{slug}/</c>。</summary>
    public static string WorkUrl(string year, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(year);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return string.Format(CultureInfo.InvariantCulture, "/works/{0}/{1}/", year, slug);
    }

    /// <summary>短文聚合页（按年份）：<c>/notes/{year}/</c>。</summary>
    public static string NoteYearUrl(string year)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(year);
        return string.Format(CultureInfo.InvariantCulture, "/notes/{0}/", year);
    }

    /// <summary>友链页：<c>/friends/</c>。</summary>
    public const string FriendsUrl = "/friends/";

    /// <summary>文章 Category 列表页：<c>/posts/categories/{slug}/</c>。</summary>
    public static string PostCategoryUrl(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return string.Format(CultureInfo.InvariantCulture, "/posts/categories/{0}/", slug);
    }

    /// <summary>把站点根相对路径与 <paramref name="baseUrl"/> 拼成绝对 URL。</summary>
    public static Uri Absolute(Uri baseUrl, string siteRelative)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteRelative);
        var normalizedBase = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        var trimmed = siteRelative.StartsWith('/') ? siteRelative[1..] : siteRelative;
        return new Uri(normalizedBase, trimmed);
    }
}
