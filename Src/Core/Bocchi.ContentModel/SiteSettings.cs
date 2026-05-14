namespace Bocchi.ContentModel;

/// <summary>导航项。</summary>
public sealed record NavigationItem(string Title, string Href);

/// <summary>社交媒体链接。</summary>
public sealed record SocialLink(string Platform, string Url);

/// <summary>作者信息。</summary>
public sealed record AuthorInfo(string Name, string? Bio = null, string? Email = null, MediaReference? Avatar = null);

/// <summary>
/// <c>robots.txt</c> 策略。M3 起由 <see cref="SiteSettings.Robots"/> 承载。
/// </summary>
/// <remarks>
/// 默认情况下站点对所有 user-agent 完全开放（<see cref="Allow"/> = <c>["/"]</c>，<see cref="Disallow"/> 为空）；
/// <c>sitemap.xml</c> 行由 <see cref="SiteSettings.EnableSitemap"/> 控制，不在此处重复。
/// </remarks>
public sealed record RobotsPolicy
{
    /// <summary>允许的路径前缀列表。空列表表示不写 <c>Allow</c> 行。</summary>
    public IReadOnlyList<string> Allow { get; init; } = ["/"];

    /// <summary>禁止的路径前缀列表。</summary>
    public IReadOnlyList<string> Disallow { get; init; } = [];
}

/// <summary>站点设置。对应 <c>Docs/Architecture.md §4.6</c>。</summary>
public sealed record SiteSettings
{
    public required string Title { get; init; }

    public string? Description { get; init; }

    public string Language { get; init; } = "zh-CN";

    public string TimeZone { get; init; } = "Asia/Shanghai";

    public required Uri BaseUrl { get; init; }

    public AuthorInfo? Author { get; init; }

    public IReadOnlyList<SocialLink> Social { get; init; } = [];

    public IReadOnlyList<NavigationItem> Navigation { get; init; } = [];

    public string? DefaultThemeId { get; init; }

    public bool EnableRss { get; init; } = true;

    public bool EnableSitemap { get; init; } = true;

    public bool EnableSearch { get; init; } = true;

    /// <summary>RSS / Atom Feed 最多包含的最新文章数。M3 默认 20。</summary>
    public int FeedItemCount { get; init; } = 20;

    /// <summary><c>robots.txt</c> 策略。</summary>
    public RobotsPolicy Robots { get; init; } = new();
}