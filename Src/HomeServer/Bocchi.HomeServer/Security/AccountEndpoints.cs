using System.Security.Claims;

using Bocchi.HomeServer.Components.Pages.Auth;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
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
        IDataProtectionProvider protection,
        DashboardLocalizationService localization)
    {
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

        SetupPendingAdminCookie.Write(context, protection, values.ToPendingAdmin());
        return Results.Redirect("/Setup/Site");
    }

    private static async Task<IResult> CompleteSetupAsync(
        HttpContext context,
        HomeServerSetupService setup,
        SignInManager<BocchiUser> signInManager,
        UserManager<BocchiUser> users,
        IDataProtectionProvider protection,
        DashboardLocalizationService localization)
    {
        if (await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            return Results.Redirect(context.User.Identity?.IsAuthenticated == true ? "/Admin" : "/Account/Login");
        }

        if (!SetupPendingAdminCookie.TryRead(context, protection, out var pendingAdmin))
        {
            SetupPendingAdminCookie.Clear(context);
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
            SetupPendingAdminCookie.Clear(context);
            return RenderSetupAdminComponent(result.Errors.Select(x => x.Description), SetupAdminFormValues.FromPendingAdmin(pendingAdmin));
        }

        SetupPendingAdminCookie.Clear(context);
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
        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var userName = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        var remember = string.Equals(form["remember"].ToString(), "on", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return RedirectToLogin(returnUrl, localization["login.error.invalid"]);
        }

        var user = await users.FindByNameAsync(userName).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return RedirectToLogin(returnUrl, localization["login.error.disabled"]);
        }

        var result = await signInManager.PasswordSignInAsync(user, password, remember, lockoutOnFailure: false).ConfigureAwait(false);
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
        [FromForm] string provider,
        [FromForm] string? returnUrl,
        ExternalLoginSettingsService settings,
        SignInManager<BocchiUser> signInManager,
        DashboardLocalizationService localization)
    {
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
        UserManager<BocchiUser> users,
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

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToLogin(returnUrl, localization["login.error.externalEmailMissing"]);
        }

        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return RedirectToLogin(returnUrl, localization["login.error.externalUserMissing"]);
        }

        var bind = await users.AddLoginAsync(user, info).ConfigureAwait(false);
        if (!bind.Succeeded && !bind.Errors.Any(x => x.Code.Contains("LoginAlreadyAssociated", StringComparison.OrdinalIgnoreCase)))
        {
            return RedirectToLogin(returnUrl, localization["login.error.externalBindFailed"]);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user).ConfigureAwait(false);
        await signInManager.SignInAsync(user, isPersistent: true, info.LoginProvider).ConfigureAwait(false);
        return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin");
    }

    private static IResult SetPublicUiLanguage(
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
}
