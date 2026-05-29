using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bocchi.HomeServer.Services;

/// <summary>后台编辑器内容资产预览端点。</summary>
public static class ContentAssetEndpoints
{
    /// <summary>注册只面向 Admin 编辑器的本地 assets 文件读取端点。</summary>
    public static IEndpointRouteBuilder MapContentAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet("/Admin/Content/Assets", ServeAsync).RequireAuthorization("Admin");
        return endpoints;
    }

    private static async Task<IResult> ServeAsync(
        string? draft,
        string? path,
        string? asset,
        ContentAssetService assets,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(asset))
        {
            return Results.NotFound();
        }

        try
        {
            var file = !string.IsNullOrWhiteSpace(draft)
                ? await assets.OpenDraftAssetAsync(draft, asset, cancellationToken).ConfigureAwait(false)
                : !string.IsNullOrWhiteSpace(path)
                    ? assets.OpenContentAsset(path, asset)
                    : null;
            return file is null
                ? Results.NotFound()
                : Results.File(file.Stream, file.ContentType, enableRangeProcessing: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return Results.NotFound();
        }
    }
}
