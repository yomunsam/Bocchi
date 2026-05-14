using System.IO.Compression;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.Workspace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Build;

/// <summary>HomeServer 注入的构建相关 HTTP 端点。</summary>
public static class BuildEndpoints
{
    /// <summary>
    /// 注册：<br/>
    /// <c>GET /build/download</c> → 把 <c>output/public/</c> 打成 zip 流给客户端。<br/>
    /// <c>GET /_bocchi/preview/{path}</c> → 触发 Live 构建并把命中 artifact 直接流回。
    /// </summary>
    public static IEndpointRouteBuilder MapBuildEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/build/download", DownloadPublicZipAsync);
        endpoints.MapGet("/_bocchi/preview/{**path}", PreviewAsync);
        return endpoints;
    }

    private static async Task DownloadPublicZipAsync(HttpContext context, WorkspaceLayout layout, CancellationToken cancellationToken)
    {
        var publicRoot = layout.PublicOutputDirectory;
        if (!Directory.Exists(publicRoot))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Output directory does not exist. Run a build first.", cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"bocchi-site-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip\"";
        using var zip = new ZipArchive(context.Response.Body, ZipArchiveMode.Create, leaveOpen: true);
        var rootFull = Path.GetFullPath(publicRoot);
        foreach (var file in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(rootFull, file).Replace(Path.DirectorySeparatorChar, '/');
            var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await fs.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PreviewAsync(string? path, HttpContext context, BuildOrchestrator orchestrator)
    {
        var siteRelative = "/" + (path ?? string.Empty);
        // 仅允许预览 Theme 输入与站点级 artifact（以 .json / .xml / .txt 结尾），媒体走静态文件
        if (!(siteRelative.EndsWith(".json", StringComparison.Ordinal)
              || siteRelative.EndsWith(".xml", StringComparison.Ordinal)
              || siteRelative.EndsWith(".txt", StringComparison.Ordinal)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var sink = new HttpStreamBuildSink(context.Response.Body, siteRelative,
            matched => context.Response.ContentType = matched.ContentType);
        var options = new BuildOptions { Mode = BuildMode.Live, OnlyArtifactPath = siteRelative };
        var result = await orchestrator.RunLiveAsync(options, themeId: null, sink, context.RequestAborted).ConfigureAwait(false);

        if (!sink.Matched)
        {
            // 流尚未写入响应；将 status 设为 404
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            if (result.Status == BuildStatus.Failed && !string.IsNullOrEmpty(result.Reason))
            {
                await context.Response.WriteAsync($"Build failed: {result.Reason}").ConfigureAwait(false);
            }
        }
    }
}
