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

    /// <summary>构造 route map 服务。</summary>
    public PreviewRouteMapService(IContentStateStore store)
    {
        _store = store;
    }

    /// <summary>列出可定位到编辑页的内容 route。</summary>
    public async Task<IReadOnlyList<PreviewRouteMapItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await _store.ListContentSummariesAsync(null, cancellationToken).ConfigureAwait(false);
        return summaries
            .Select(Map)
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

    private static PreviewRouteMapItem? Map(ContentSummary summary)
    {
        var slug = summary.ContentId;
        var route = summary.Kind switch
        {
            ContentKind.Post when !string.IsNullOrWhiteSpace(summary.Year) => SiteUrlResolver.PostUrl(summary.Year, slug),
            ContentKind.Page => SiteUrlResolver.PageUrl(slug),
            ContentKind.Work when !string.IsNullOrWhiteSpace(summary.Year) => SiteUrlResolver.WorkUrl(summary.Year, slug),
            ContentKind.FriendLink => SiteUrlResolver.FriendsUrl,
            ContentKind.SiteSettings => "/",
            _ => null,
        };

        return route is null
            ? null
            : new PreviewRouteMapItem(route, summary.Kind.ToString(), summary.Title ?? summary.ContentId, ContentEditingService.EditUrl(summary.RelativePath));
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
