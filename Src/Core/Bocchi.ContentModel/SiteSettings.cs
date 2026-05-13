namespace Bocchi.ContentModel;

/// <summary>导航项。</summary>
public sealed record NavigationItem(string Title, string Href);

/// <summary>社交媒体链接。</summary>
public sealed record SocialLink(string Platform, string Url);

/// <summary>作者信息。</summary>
public sealed record AuthorInfo(string Name, string? Bio = null, string? Email = null, MediaReference? Avatar = null);

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
}
