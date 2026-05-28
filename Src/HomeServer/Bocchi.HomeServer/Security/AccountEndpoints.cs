using Bocchi.HomeServer.Components.Pages.Auth;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Bocchi.HomeServer.Security;

/// <summary>
/// Setup、Login、Logout 与外部登录回调端点。UI 已迁入 Razor components；这里仅保留必须写 Cookie/redirect 的 HTTP 行为。
/// </summary>
public static class AccountEndpoints
{
    /// <summary>注册账户与 Setup 相关端点。</summary>
    public static IEndpointRouteBuilder MapBocchiAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/Setup/Admin", SubmitSetupAdminAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapGet("/Setup/Complete", () => Results.Redirect("/Setup/Site")).AllowAnonymous();
        endpoints.MapPost("/Setup/Complete", CompleteSetupAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapPost("/Setup/UiLanguage", SetPublicUiLanguage).AllowAnonymous().DisableAntiforgery();
        endpoints.MapPost("/Account/UiLanguage", SetPublicUiLanguage).AllowAnonymous().DisableAntiforgery();
        endpoints.MapPost("/Account/Login/Submit", SubmitLoginAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapPost("/Account/Logout", LogoutAsync).RequireAuthorization();
        endpoints.MapPost("/Account/ExternalLogin", BeginExternalLoginAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapGet("/Account/ExternalLoginCallback", CompleteExternalLoginAsync).AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> SubmitSetupAdminAsync(
        HttpContext context,
        HomeServerSetupService setup,
        SetupPendingAdminStore pendingAdmins,
        DashboardLocalizationService localization)
    {
        if (RejectCrossSiteFormPost(context) is { } rejected)
        {
            return rejected;
        }

        if (await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            return Results.Redirect(context.User.Identity?.IsAuthenticated == true ? "/Admin" : "/Account/Login");
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var values = SetupAdminFormValues.FromForm(form);
        var errors = ValidateAdmin(values, localization);

        if (errors.Count > 0)
        {
            return RenderSetupAdminComponent(errors, values);
        }

        pendingAdmins.Write(context, values.ToPendingAdmin());
        return Results.Redirect("/Setup/Site");
    }

    private static async Task<IResult> CompleteSetupAsync(
        HttpContext context,
        HomeServerSetupService setup,
        SignInManager<BocchiUser> signInManager,
        UserManager<BocchiUser> users,
        SetupPendingAdminStore pendingAdmins,
        DashboardLocalizationService localization)
    {
        if (RejectCrossSiteFormPost(context) is { } rejected)
        {
            return rejected;
        }

        if (await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            return Results.Redirect(context.User.Identity?.IsAuthenticated == true ? "/Admin" : "/Account/Login");
        }

        if (!pendingAdmins.TryRead(context, out var pendingAdmin))
        {
            pendingAdmins.Clear(context);
            return Results.Redirect("/Setup?message=" + Uri.EscapeDataString(localization["setup.error.payloadExpired"]));
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var values = SetupSiteFormValues.FromForm(form);
        var errors = ValidateSite(values, localization);

        if (errors.Count > 0)
        {
            return RenderSetupSiteComponent(errors, values);
        }

        var result = await setup.CreateFirstAdminAsync(
            pendingAdmin.UserName,
            pendingAdmin.Password,
            pendingAdmin.DisplayName,
            pendingAdmin.Email,
            values.ToSiteProfileUpdate(),
            context.RequestAborted).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            pendingAdmins.Clear(context);
            return RenderSetupAdminComponent(result.Errors.Select(x => x.Description), SetupAdminFormValues.FromPendingAdmin(pendingAdmin));
        }

        pendingAdmins.Clear(context);
        var user = await users.FindByNameAsync(pendingAdmin.UserName).ConfigureAwait(false);
        if (user is not null)
        {
            await signInManager.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        }

        return Results.Redirect("/Admin");
    }

    private static async Task<IResult> SubmitLoginAsync(
        HttpContext context,
        UserManager<BocchiUser> users,
        SignInManager<BocchiUser> signInManager,
        DashboardLocalizationService localization,
        [FromQuery(Name = "returnUrl")] string? returnUrl)
    {
        if (RejectCrossSiteFormPost(context) is { } rejected)
        {
            return rejected;
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var userName = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        var remember = string.Equals(form["remember"].ToString(), "on", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return RedirectToLogin(returnUrl, localization["login.error.invalid"]);
        }

        var user = await users.FindByNameAsync(userName).ConfigureAwait(false);
        if (user is null)
        {
            return RedirectToLogin(returnUrl, localization["login.error.invalid"]);
        }

        if (user.IsDisabled)
        {
            return RedirectToLogin(returnUrl, localization["login.error.disabled"]);
        }

        if (!user.LockoutEnabled)
        {
            await users.SetLockoutEnabledAsync(user, true).ConfigureAwait(false);
        }

        var result = await signInManager.PasswordSignInAsync(user, password, remember, lockoutOnFailure: true).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return RedirectToLogin(returnUrl, localization["login.error.invalid"]);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user).ConfigureAwait(false);
        return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin");
    }

    private static async Task<IResult> LogoutAsync(SignInManager<BocchiUser> signInManager)
    {
        await signInManager.SignOutAsync().ConfigureAwait(false);
        return Results.Redirect("/Account/Login");
    }

    private static async Task<IResult> BeginExternalLoginAsync(
        HttpContext context,
        [FromForm] string provider,
        [FromForm] string? returnUrl,
        ExternalLoginSettingsService settings,
        SignInManager<BocchiUser> signInManager,
        DashboardLocalizationService localization)
    {
        if (RejectCrossSiteFormPost(context) is { } rejected)
        {
            return rejected;
        }

        var ready = await settings.ListReadyForLoginAsync().ConfigureAwait(false);
        if (!ready.Any(x => string.Equals(x.ProviderKey, provider, StringComparison.OrdinalIgnoreCase)))
        {
            return RedirectToLogin(returnUrl, localization["login.error.providerNotReady"]);
        }

        var redirectUrl = "/Account/ExternalLoginCallback?returnUrl=" + Uri.EscapeDataString(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin");
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Results.Challenge(properties, [provider]);
    }

    private static async Task<IResult> CompleteExternalLoginAsync(
        [FromQuery(Name = "returnUrl")] string? returnUrl,
        SignInManager<BocchiUser> signInManager,
        DashboardLocalizationService localization)
    {
        var info = await signInManager.GetExternalLoginInfoAsync().ConfigureAwait(false);
        if (info is null)
        {
            return RedirectToLogin(returnUrl, localization["login.error.externalMissing"]);
        }

        var signIn = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: true,
            bypassTwoFactor: true).ConfigureAwait(false);
        if (signIn.Succeeded)
        {
            return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin");
        }

        return RedirectToLogin(returnUrl, localization["login.error.externalLoginNotLinked"]);
    }

    private static IResult SetPublicUiLanguage(
        HttpContext context,
        [FromForm] string uiLanguage,
        [FromForm] string? returnUrl,
        DashboardLocalizationService localization)
    {
        if (RejectCrossSiteFormPost(context) is { } rejected)
        {
            return rejected;
        }

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

        return Results.LocalRedirect(IsLocalUrl(returnUrl) ? returnUrl! : "/Setup");
    }

    /// <summary>校验第一页 Admin 输入；Identity 的最终校验仍在创建账号时执行。</summary>
    private static List<string> ValidateAdmin(SetupAdminFormValues values, DashboardLocalizationService localization)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(values.UserName))
        {
            errors.Add(localization["setup.error.usernameRequired"]);
        }

        if (!string.IsNullOrWhiteSpace(values.Email) && !IsValidEmail(values.Email))
        {
            errors.Add(localization["setup.error.emailInvalid"]);
        }

        if (values.Password.Length < 8)
        {
            errors.Add(localization["setup.error.passwordLength"]);
        }

        if (!string.Equals(values.Password, values.ConfirmPassword, StringComparison.Ordinal))
        {
            errors.Add(localization["setup.error.passwordConfirm"]);
        }

        return errors;
    }

    /// <summary>校验第二页站点基础约定，避免把不完整的服务器基础事实写入数据库。</summary>
    private static List<string> ValidateSite(SetupSiteFormValues values, DashboardLocalizationService localization)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(values.SiteName))
        {
            errors.Add(localization["setup.error.siteNameRequired"]);
        }

        if (!IsPublicBaseUrl(values.PublicBaseUrl))
        {
            errors.Add(localization["setup.error.publicUrlInvalid"]);
        }

        return errors;
    }

    /// <summary>以 Razor component 重新渲染 Setup 第一步并带回非密码字段。</summary>
    private static RazorComponentResult<Setup> RenderSetupAdminComponent(
        IEnumerable<string> errors,
        SetupAdminFormValues values)
        => new(new
        {
            Errors = errors.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            values.UserName,
            values.Email,
            values.DisplayName,
        });

    /// <summary>以 Razor component 重新渲染 Setup 第二步并带回用户提交值。</summary>
    private static RazorComponentResult<SetupSite> RenderSetupSiteComponent(
        IEnumerable<string> errors,
        SetupSiteFormValues values)
        => new(new
        {
            Errors = errors.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            HasSubmittedValues = true,
            values.SiteName,
            values.DefaultTitle,
            values.Description,
            values.PublicBaseUrl,
            values.CopyrightNotice,
            values.DefaultThemeId,
        });

    /// <summary>带本地化消息回到 Login 页面。</summary>
    private static IResult RedirectToLogin(string? returnUrl, string message)
    {
        var target = "/Account/Login?message=" + Uri.EscapeDataString(message);
        if (IsLocalUrl(returnUrl))
        {
            target += "&returnUrl=" + Uri.EscapeDataString(returnUrl!);
        }

        return Results.Redirect(target);
    }

    private static bool IsPublicBaseUrl(string value)
        => string.IsNullOrWhiteSpace(value) ||
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";

    private static bool IsValidEmail(string value)
        => value.Count(c => c == '@') == 1
            && value.IndexOf('@') > 0
            && value.IndexOf('@') < value.Length - 1;

    private static bool IsLocalUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal);

    /// <summary>对仍禁用 antiforgery token 的账户表单做同源守卫；缺少浏览器元数据时保留本地测试和 CLI 客户端兼容性。</summary>
    private static IResult? RejectCrossSiteFormPost(HttpContext context)
    {
        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest();
        }

        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) && !IsSameOrigin(context, origin))
        {
            return Results.BadRequest();
        }

        var referer = context.Request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer) && !IsSameOrigin(context, referer))
        {
            return Results.BadRequest();
        }

        return null;
    }

    /// <summary>比较请求来源是否与当前 Host 同源；用于补足没有 antiforgery token 的 SSR 表单边界。</summary>
    private static bool IsSameOrigin(HttpContext context, string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var request = context.Request;
        var requestPort = request.Host.Port ?? DefaultPort(request.Scheme);
        var sourcePort = uri.IsDefaultPort ? DefaultPort(uri.Scheme) : uri.Port;
        return string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && sourcePort == requestPort;
    }

    private static int DefaultPort(string scheme)
        => string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}
