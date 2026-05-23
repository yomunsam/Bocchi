using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Dashboard UI language 的普通 HTTP 端点。设置 culture cookie 必须走响应管线，不能放在 Blazor 事件里。
/// </summary>
public static class DashboardLocalizationEndpoints
{
    /// <summary>注册 Dashboard 本地化相关端点。</summary>
    public static IEndpointRouteBuilder MapDashboardLocalizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/Admin/Settings/Localization/UiLanguage", SetDashboardUiLanguage)
            .RequireAuthorization("Admin")
            .DisableAntiforgery();

        return endpoints;
    }

    /// <summary>写入 Dashboard UI culture cookie，并跳回调用前的本地 Dashboard URL。</summary>
    private static IResult SetDashboardUiLanguage(
        HttpContext context,
        [FromForm] string uiLanguage,
        [FromForm] string? returnUrl,
        DashboardLocalizationService localization)
    {
        var language = localization.ResolveLanguage(uiLanguage);
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(language.Code)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
            });

        return Results.LocalRedirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/Admin/Settings/Localization");
    }

    /// <summary>只允许站内相对跳转，避免语言切换端点成为开放重定向入口。</summary>
    private static bool IsLocalReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal);
}
