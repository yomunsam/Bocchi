using Bocchi.ContentModel;
using Bocchi.Generator.ContentGraph;
using Bocchi.Workspace.State;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 预览 route 到内容文件的轻量映射服务。M4 用它支撑 Preview Toolbar 的 Edit 入口。
/// </summary>
public sealed class PreviewRouteMapService
{
    private readonly IContentStateStore _store;
    private readonly LocalizationSettingsService _localization;

    /// <summary>构造 route map 服务。</summary>
    public PreviewRouteMapService(IContentStateStore store, LocalizationSettingsService localization)
    {
        _store = store;
        _localization = localization;
    }

    /// <summary>列出可定位到编辑页的内容 route。</summary>
    public async Task<IReadOnlyList<PreviewRouteMapItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await _store.ListContentSummariesAsync(null, cancellationToken).ConfigureAwait(false);
        var settings = await _localization.GetAsync(cancellationToken).ConfigureAwait(false);
        var primaryLanguage = settings.PrimaryLanguage.Code;
        return summaries
            .Select(summary => Map(summary, primaryLanguage))
            .Where(x => x is not null)
            .Cast<PreviewRouteMapItem>()
            .OrderBy(x => x.Route, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>根据当前预览 route 查找编辑入口。</summary>
    public async Task<PreviewRouteMapItem?> FindAsync(string route, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRoute(route);
        return (await ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(x => string.Equals(NormalizeRoute(x.Route), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static PreviewRouteMapItem? Map(ContentSummary summary, string primaryLanguage)
    {
        var route = ResolveRoute(summary, primaryLanguage);
        return route is null
            ? null
            : new PreviewRouteMapItem(route, summary.Kind.ToString(), summary.Title ?? summary.ContentId, ContentEditingService.EditUrl(summary.RelativePath));
    }

    /// <summary>按 M6 PrimaryUnprefixed 策略从 state store 摘要恢复前台 route。</summary>
    private static string? ResolveRoute(ContentSummary summary, string primaryLanguage)
    {
        var route = summary.Kind switch
        {
            ContentKind.Post => ResolvePostRoute(summary),
            ContentKind.Page => ResolvePageRoute(summary),
            ContentKind.Work => ResolveWorkRoute(summary),
            ContentKind.FriendLink => SiteUrlResolver.FriendsUrl,
            ContentKind.SiteSettings => "/",
            _ => null,
        };

        if (route is null)
        {
            return null;
        }

        var language = string.IsNullOrWhiteSpace(summary.Language) ? primaryLanguage : summary.Language.Trim();
        return ApplyPrimaryUnprefixedUrlPolicy(route, language, primaryLanguage);
    }

    private static string? ResolvePostRoute(ContentSummary summary)
    {
        if (TrySplitGroup(summary.LocalizationGroup, "posts", out var year, out var slug))
        {
            return SiteUrlResolver.PostUrl(year, slug);
        }

        return string.IsNullOrWhiteSpace(summary.Year)
            ? null
            : SiteUrlResolver.PostUrl(summary.Year, ResolveLegacySlug(summary.ContentId));
    }

    private static string? ResolvePageRoute(ContentSummary summary)
    {
        if (TrySplitGroup(summary.LocalizationGroup, "pages", out var slug))
        {
            return SiteUrlResolver.PageUrl(slug);
        }

        return SiteUrlResolver.PageUrl(ResolveLegacySlug(summary.ContentId));
    }

    private static string? ResolveWorkRoute(ContentSummary summary)
    {
        if (TrySplitGroup(summary.LocalizationGroup, "works", out var year, out var slug))
        {
            return SiteUrlResolver.WorkUrl(year, slug);
        }

        return string.IsNullOrWhiteSpace(summary.Year)
            ? null
            : SiteUrlResolver.WorkUrl(summary.Year, ResolveLegacySlug(summary.ContentId));
    }

    private static bool TrySplitGroup(string? group, string expectedKind, out string slug)
    {
        slug = string.Empty;
        var parts = SplitGroup(group);
        if (parts.Length != 2 || !string.Equals(parts[0], expectedKind, StringComparison.Ordinal))
        {
            return false;
        }

        slug = parts[1];
        return true;
    }

    private static bool TrySplitGroup(string? group, string expectedKind, out string year, out string slug)
    {
        year = string.Empty;
        slug = string.Empty;
        var parts = SplitGroup(group);
        if (parts.Length != 3 || !string.Equals(parts[0], expectedKind, StringComparison.Ordinal))
        {
            return false;
        }

        year = parts[1];
        slug = parts[2];
        return true;
    }

    private static string[] SplitGroup(string? group)
        => string.IsNullOrWhiteSpace(group)
            ? []
            : group.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ResolveLegacySlug(string contentId)
    {
        var normalized = string.IsNullOrWhiteSpace(contentId) ? "index" : contentId.Trim();
        var languageSeparator = normalized.LastIndexOf('@');
        if (languageSeparator > 0)
        {
            normalized = normalized[..languageSeparator];
        }

        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string ApplyPrimaryUnprefixedUrlPolicy(string route, string language, string primaryLanguage)
    {
        if (string.Equals(language, primaryLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return route;
        }

        var normalized = route.StartsWith('/') ? route : "/" + route;
        return normalized == "/"
            ? $"/{language}/"
            : $"/{language}{normalized}";
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = string.IsNullOrWhiteSpace(route) ? "/" : route.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.EndsWith('/') || Path.HasExtension(normalized)
            ? normalized
            : normalized + "/";
    }
}

/// <summary>一条前台 route 与后台编辑入口的映射。</summary>
/// <param name="Route">前台站点 route。</param>
/// <param name="Kind">内容类型。</param>
/// <param name="Title">用于 Dashboard 展示的标题。</param>
/// <param name="EditUrl">后台编辑页 URL。</param>
public sealed record PreviewRouteMapItem(string Route, string Kind, string Title, string EditUrl);
