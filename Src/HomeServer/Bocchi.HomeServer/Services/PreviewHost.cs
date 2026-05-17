using System.Net;

using Bocchi.Workspace;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Home Server 内置前台预览 Host。它从 output/public 读取构建产物，并给 HTML 注入轻量 Preview Toolbar。
/// </summary>
public sealed class PreviewHost
{
    private readonly BocchiDataLayout _layout;
    private readonly PreviewRouteMapService _routeMap;

    /// <summary>构造预览 Host。</summary>
    public PreviewHost(BocchiDataLayout layout, PreviewRouteMapService routeMap)
    {
        _layout = layout;
        _routeMap = routeMap;
    }

    /// <summary>渲染受保护预览页面或静态资源。</summary>
    public async Task<IResult> RenderAsync(string? previewPath, CancellationToken cancellationToken = default)
    {
        var route = "/" + (previewPath ?? string.Empty).TrimStart('/');
        if (!TryResolvePublicFile(route, out var filePath))
        {
            return Results.Content(await RenderMissingPreviewAsync(route, cancellationToken).ConfigureAwait(false), "text/html; charset=utf-8");
        }

        var contentType = GuessContentType(filePath);
        if (!contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return Results.File(filePath, contentType);
        }

        var html = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var injected = await InjectToolbarAsync(html, route, cancellationToken).ConfigureAwait(false);
        return Results.Content(injected, "text/html; charset=utf-8");
    }

    private bool TryResolvePublicFile(string route, out string filePath)
    {
        filePath = string.Empty;
        var normalized = route.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var root = Path.GetFullPath(_layout.PublicOutputDirectory);
        var candidates = new List<string>();
        if (string.IsNullOrEmpty(normalized))
        {
            candidates.Add(Path.Combine(root, "index.html"));
        }
        else if (Path.HasExtension(normalized))
        {
            candidates.Add(Path.Combine(root, normalized));
        }
        else
        {
            candidates.Add(Path.Combine(root, normalized, "index.html"));
            candidates.Add(Path.Combine(root, normalized + ".html"));
        }

        filePath = candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            ?? string.Empty;
        return filePath.Length > 0;
    }

    private async Task<string> RenderMissingPreviewAsync(string route, CancellationToken cancellationToken)
    {
        var toolbar = await RenderToolbarAsync(route, cancellationToken).ConfigureAwait(false);
        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Preview · Bocchi</title>
                <link rel="stylesheet" href="/app.css">
            </head>
            <body>
                <main class="bocchi-preview-empty">
                    <p class="bocchi-page-heading__eyebrow">Preview</p>
                    <h1>还没有可预览的前台页面</h1>
                    <p>先在 Publish 页面跑一次构建；如果当前还没有可用 Theme，M4 会保持这个受保护状态页，而不临时发明一套前台主题。</p>
                    <p><a class="bocchi-button secondary" href="/Admin/Publish">Open Publish</a></p>
                </main>
                {{toolbar}}
            </body>
            </html>
            """;
    }

    private async Task<string> InjectToolbarAsync(string html, string route, CancellationToken cancellationToken)
    {
        var toolbar = await RenderToolbarAsync(route, cancellationToken).ConfigureAwait(false);
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd < 0
            ? html + toolbar
            : html.Insert(bodyEnd, toolbar);
    }

    private async Task<string> RenderToolbarAsync(string route, CancellationToken cancellationToken)
    {
        var map = await _routeMap.FindAsync(route, cancellationToken).ConfigureAwait(false);
        var safeRoute = WebUtility.HtmlEncode(route);
        var edit = map is null
            ? string.Empty
            : $"""<a class="bocchi-preview-toolbar__button" href="{WebUtility.HtmlEncode(map.EditUrl)}">Edit</a>""";

        return $$$"""
            <style>
            .bocchi-preview-toolbar{position:fixed;right:1rem;bottom:1rem;z-index:2147483000;display:flex;align-items:center;gap:.45rem;border:1px solid #e8e4ef;border-radius:8px;background:#fff;color:#26253a;box-shadow:0 14px 34px rgba(77,67,107,.16);font:14px/1.35 Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;padding:.55rem}
            .bocchi-preview-toolbar__status{border-radius:999px;background:#f2f7ff;color:#407fc2;font-weight:760;padding:.28rem .58rem}
            .bocchi-preview-toolbar__route{max-width:min(38vw,22rem);overflow:hidden;color:#747186;text-overflow:ellipsis;white-space:nowrap}
            .bocchi-preview-toolbar__button{border:1px solid #e8e4ef;border-radius:8px;background:#fff;color:#26253a;font-weight:760;padding:.42rem .64rem;text-decoration:none}
            @media (max-width:640px){.bocchi-preview-toolbar{left:.75rem;right:.75rem;bottom:.75rem;flex-wrap:wrap}.bocchi-preview-toolbar__route{max-width:100%}}
            </style>
            <nav class="bocchi-preview-toolbar" aria-label="Bocchi preview toolbar">
                <span class="bocchi-preview-toolbar__status">Preview</span>
                <span class="bocchi-preview-toolbar__route">{{{safeRoute}}}</span>
                <a class="bocchi-preview-toolbar__button" href="/Admin">Admin</a>
                <a class="bocchi-preview-toolbar__button" href="/Admin/Publish">Rebuild</a>
                {{{edit}}}
            </nav>
            """;
    }

    private static string GuessContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".xml" => "application/xml; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
}
