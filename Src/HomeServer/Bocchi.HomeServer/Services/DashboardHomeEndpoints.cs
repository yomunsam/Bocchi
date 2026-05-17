using Microsoft.AspNetCore.Mvc;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Dashboard 首页 SSR 表单端点：发布首页 composer 的短文。
/// 它们都走传统 HTTP form post，与现有 Settings / Logout 风格保持一致。
/// </summary>
public static class DashboardHomeEndpoints
{
    /// <summary>注册首页表单端点。</summary>
    public static IEndpointRouteBuilder MapDashboardHomeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/Admin/Home/Notes", PublishNoteAsync)
            .RequireAuthorization("Admin")
            .DisableAntiforgery();

        return endpoints;
    }

    /// <summary>发布一条短文；成功后跳回 <c>/Admin</c>，失败时把错误以查询参数回传。</summary>
    private static async Task<IResult> PublishNoteAsync(
        [FromForm] string? body,
        [FromForm] string? returnUrl,
        NoteCreationService notes,
        CancellationToken cancellationToken)
    {
        var target = LocalReturnUrl(returnUrl) ?? "/Admin";
        try
        {
            await notes.CreateAsync(body ?? string.Empty, cancellationToken).ConfigureAwait(false);
            return Results.LocalRedirect(AppendQuery(target, "notePosted", "1"));
        }
        catch (InvalidOperationException ex)
        {
            return Results.LocalRedirect(AppendQuery(target, "noteError", ex.Message));
        }
    }

    /// <summary>只允许站内相对跳转。</summary>
    private static string? LocalReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            ? returnUrl
            : null;

    /// <summary>在已有 URL 上安全地追加一个查询参数。</summary>
    private static string AppendQuery(string url, string key, string value)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }
}
