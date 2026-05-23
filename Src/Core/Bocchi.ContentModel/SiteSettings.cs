namespace Bocchi.ContentModel;

/// <summary>前台 Menu target。它保存用户选择的语义目标，而不是 Theme 最终渲染出的 URL。</summary>
public sealed record NavigationTarget
{
    /// <summary>目标类型。v1 固定为 builtin、themePage、page、postCategory，未知值会在生成时视为 unresolved。</summary>
    public required string Type { get; init; }

    /// <summary>目标值。builtin 使用内置名称，page 使用页面 slug，postCategory 使用稳定 slug，themePage 使用特殊页面 name。</summary>
    public string? Value { get; init; }
}

/// <summary>前台 Menu tree 节点。Theme 自行决定如何表达桌面端、移动端和嵌套层级。</summary>
public sealed record NavigationItem
{
    /// <summary>节点稳定 id，Dashboard 用它保持编辑器列表和保存结果稳定。</summary>
    public required string Id { get; init; }

    /// <summary>展示标签；可以是普通文本，也可以是 <c>i18n://common@key</c> / <c>i18n://theme@key</c>。</summary>
    public string? Label { get; init; }

    /// <summary>导航目标。生成器会基于当前内容图与 Theme manifest 解析为 URL。</summary>
    public required NavigationTarget Target { get; init; }

    /// <summary>子 Menu 项。最大深度与 Category tree 保持一致，由读写服务裁剪为 5 层。</summary>
    public IReadOnlyList<NavigationItem> Children { get; init; } = [];
}

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

    /// <summary>前台页面缺少专用标题时使用的默认标题。</summary>
    public string? DefaultTitle { get; init; }

    public string? Description { get; init; }

    public string Language { get; init; } = "zh-CN";

    public string TimeZone { get; init; } = "Asia/Shanghai";

    public required Uri BaseUrl { get; init; }

    /// <summary>前台 footer 和 Theme Context 使用的版权文案。</summary>
    public string? CopyrightNotice { get; init; }

    public AuthorInfo? Author { get; init; }

    public IReadOnlyList<SocialLink> Social { get; init; } = [];

    /// <summary>站点级 primary menu。Theme 可以渲染为顶栏、抽屉或任意嵌套导航形态。</summary>
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
