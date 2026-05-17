using System.IO.Compression;

using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.Generator.State;
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
    /// <c>GET /Admin/Publish/download</c> → 把 <c>output/public/</c> 打成 zip 流给客户端。<br/>
    /// <c>POST /Admin/Publish/run</c> → 触发一次 Full Build，便于自动化测试和非 Blazor 客户端。<br/>
    /// <c>GET /_bocchi/preview/{path}</c> → 触发 Live 构建并把命中 artifact 直接流回。
    /// </summary>
    public static IEndpointRouteBuilder MapBuildEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var publish = endpoints.MapGroup("/Admin/Publish").RequireAuthorization("Admin");
        publish.MapGet("/download", DownloadPublicZipAsync);
        publish.MapPost("/run", RunBuildAsync).DisableAntiforgery();
        endpoints.MapGet("/_bocchi/preview/{**path}", PreviewAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> RunBuildAsync(BuildOrchestrator orchestrator, CancellationToken cancellationToken)
    {
        var result = await orchestrator.RunFullBuildAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Environment = "production" },
            themeId: null,
            cancellationToken).ConfigureAwait(false);

        return Results.Json(new
        {
            result.SessionId,
            Status = result.Status.ToString(),
            Fingerprint = result.Fingerprint?.Value,
            result.Reason,
            ArtifactCount = result.Artifacts.Count,
        });
    }

    private static async Task DownloadPublicZipAsync(
        HttpContext context,
        BocchiDataLayout layout,
        IBuildStateStore store,
        CancellationToken cancellationToken)
    {
        var latestSuccessful = await store.GetLatestSuccessfulRunAsync(cancellationToken).ConfigureAwait(false);
        if (latestSuccessful is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("No successful build exists. Run a build first.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var publicRoot = layout.PublicOutputDirectory;
        if (!Directory.Exists(publicRoot))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Output directory does not exist. Run a build first.", cancellationToken).ConfigureAwait(false);
            return;
        }

        context.Response.ContentType = "application/zip";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"bocchi-site-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip\"";
        var rootFull = Path.GetFullPath(publicRoot);
        var files = Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Output directory is empty. Run a build first.", cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(rootFull, file).Replace(Path.DirectorySeparatorChar, '/');
                var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                await fs.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            }
        }

        buffer.Position = 0;
        context.Response.ContentLength = buffer.Length;
        await buffer.CopyToAsync(context.Response.Body, cancellationToken).ConfigureAwait(false);
    }

    private static async Task PreviewAsync(string? path, HttpContext context, BuildOrchestrator orchestrator)
    {
        if (!TryMapPreviewPath(path, out var artifactPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var sink = new HttpStreamBuildSink(context.Response.Body, artifactPath,
            matched =>
            {
                context.Response.ContentType = matched.ContentType;
                context.Response.Headers.ETag = $"\"sha256-{matched.Sha256}\"";
            });
        var options = new BuildOptions { Mode = BuildMode.Live, OnlyArtifactPath = artifactPath };
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

    private static bool TryMapPreviewPath(string? rawPath, out string artifactPath)
    {
        artifactPath = string.Empty;
        var path = (rawPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.StartsWith("data/", StringComparison.Ordinal))
        {
            var name = path["data/".Length..];
            if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
            {
                return false;
            }

            artifactPath = "/" + name;
            return true;
        }

        if (path.StartsWith("media/", StringComparison.Ordinal))
        {
            artifactPath = "/" + path;
            return true;
        }

        if (string.Equals(path, "robots.txt", StringComparison.Ordinal)
            || string.Equals(path, "sitemap.xml", StringComparison.Ordinal)
            || string.Equals(path, "feed.xml", StringComparison.Ordinal)
            || string.Equals(path, ".bocchi-manifest.json", StringComparison.Ordinal))
        {
            artifactPath = "/" + path;
            return true;
        }

        return false;
    }
}
