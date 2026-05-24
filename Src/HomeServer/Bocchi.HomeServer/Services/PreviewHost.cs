using System.Net;
using System.Text;

using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Build;
using Bocchi.Workspace;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Home Server 内置前台预览 Host。它通过 Live build 生成当前请求的前台 artifact，并给 HTML 注入轻量 Preview Toolbar。
/// </summary>
public sealed class PreviewHost
{
    private readonly BocchiDataLayout _layout;
    private readonly BuildOrchestrator _orchestrator;
    private readonly PreviewRouteMapService _routeMap;

    /// <summary>构造预览 Host。</summary>
    public PreviewHost(BocchiDataLayout layout, BuildOrchestrator orchestrator, PreviewRouteMapService routeMap)
    {
        _layout = layout;
        _orchestrator = orchestrator;
        _routeMap = routeMap;
    }

    /// <summary>渲染受保护实时预览页面或前台资源。</summary>
    public async Task<IResult> RenderAsync(string? previewPath, CancellationToken cancellationToken = default)
    {
        var route = "/" + (previewPath ?? string.Empty).TrimStart('/');
        if (!TryMapPreviewRoute(route, out var artifactPath))
        {
            return Results.NotFound();
        }

        var liveRoot = Path.Combine(_layout.CacheDirectory, "live-preview", Guid.NewGuid().ToString("N"));
        var themeInputDirectory = Path.Combine(liveRoot, "input");
        var themeOutputDirectory = Path.Combine(liveRoot, "output");
        var sink = new LivePreviewBuildSink(themeInputDirectory, artifactPath);
        try
        {
            var result = await _orchestrator.RunLiveAsync(
                new BuildOptions
                {
                    Mode = BuildMode.Live,
                    Environment = "development",
                    OnlyArtifactPath = artifactPath,
                    LiveThemeInputDirectory = themeInputDirectory,
                    LiveThemeOutputDirectory = themeOutputDirectory,
                },
                themeId: null,
                sink,
                cancellationToken).ConfigureAwait(false);

            if (!sink.Matched)
            {
                var statusCode = result.Status == BuildStatus.Failed
                    ? StatusCodes.Status500InternalServerError
                    : StatusCodes.Status404NotFound;
                return Results.Content(
                    await RenderMissingPreviewAsync(route, result.Reason, cancellationToken).ConfigureAwait(false),
                    "text/html; charset=utf-8",
                    statusCode: statusCode);
            }

            var artifact = sink.MatchedArtifact!;
            var bytes = sink.GetMatchedBytes();
            if (!artifact.ContentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Bytes(bytes, artifact.ContentType);
            }

            var html = Encoding.UTF8.GetString(bytes);
            var injected = await InjectToolbarAsync(html, route, cancellationToken).ConfigureAwait(false);
            return Results.Content(injected, "text/html; charset=utf-8");
        }
        finally
        {
            DeleteLiveWorkingDirectory(liveRoot);
        }
    }

    /// <summary>把前台路由映射到 Live build 的 artifact 路径。</summary>
    private static bool TryMapPreviewRoute(string route, out string artifactPath)
    {
        artifactPath = string.Empty;
        var normalized = route.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(normalized))
        {
            artifactPath = "/index.html";
            return true;
        }

        if (Path.HasExtension(normalized))
        {
            artifactPath = "/" + normalized;
            return true;
        }

        artifactPath = "/" + normalized.TrimEnd('/') + "/index.html";
        return true;
    }

    /// <summary>渲染 Live build 未命中或失败时的受保护状态页。</summary>
    private async Task<string> RenderMissingPreviewAsync(string route, string? reason, CancellationToken cancellationToken)
    {
        var toolbar = await RenderToolbarAsync(route, cancellationToken).ConfigureAwait(false);
        var detail = string.IsNullOrWhiteSpace(reason)
            ? "当前路由没有对应的 Theme 输出。可以回到 Admin 选择内容继续编辑，或确认当前 Theme 是否提供这个页面。"
            : "实时预览生成失败：" + WebUtility.HtmlEncode(reason);
        return $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Preview · Bocchi</title>
                <link rel="icon" href="/favicon.ico" sizes="any">
                <link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png">
                <link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png">
                <link rel="icon" type="image/png" sizes="48x48" href="/favicon-48x48.png">
                <link rel="apple-touch-icon" sizes="180x180" href="/apple-touch-icon.png">
                <link rel="manifest" href="/site.webmanifest">
                <meta name="theme-color" content="#f8f7fc">
                <link rel="stylesheet" href="/app.css">
            </head>
            <body>
                <main class="bocchi-preview-empty">
                    <p class="bocchi-page-heading__eyebrow">Preview</p>
                    <h1>暂时没有这个预览页面</h1>
                    <p>{{detail}}</p>
                    <p><a class="bocchi-button secondary" href="/Admin">Back to Admin</a></p>
                </main>
                {{toolbar}}
            </body>
            </html>
            """;
    }

    /// <summary>向 HTML 响应末尾注入 Home Server 控制的 Preview Toolbar。</summary>
    private async Task<string> InjectToolbarAsync(string html, string route, CancellationToken cancellationToken)
    {
        var toolbar = await RenderToolbarAsync(route, cancellationToken).ConfigureAwait(false);
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd < 0
            ? html + toolbar
            : html.Insert(bodyEnd, toolbar);
    }

    /// <summary>渲染轻量浮动工具栏；编辑入口只在 route map 能明确匹配时出现。</summary>
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
                {{{edit}}}
            </nav>
            """;
    }

    /// <summary>清理本次 Live 预览的临时工作目录。</summary>
    private static void DeleteLiveWorkingDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时预览目录清理失败不应影响本次响应；后续预览会使用新的目录。
        }
    }
}
