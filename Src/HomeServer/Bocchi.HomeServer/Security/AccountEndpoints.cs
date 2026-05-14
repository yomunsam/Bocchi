using System.Net;
using System.Security.Claims;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Bocchi.HomeServer.Security;

/// <summary>
/// Setup、Login、Logout 与外部登录回调端点。Cookie 写入必须走普通 HTTP 端点，不放进 interactive component。
/// </summary>
public static class AccountEndpoints
{
    /// <summary>注册账户与 Setup 相关端点。</summary>
    public static IEndpointRouteBuilder MapBocchiAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/Setup", RenderSetupAsync).AllowAnonymous();
        endpoints.MapPost("/Setup", SubmitSetupAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapGet("/Account/Login", RenderLoginAsync).AllowAnonymous();
        endpoints.MapPost("/Account/Login", SubmitLoginAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapPost("/Account/Logout", LogoutAsync).RequireAuthorization();
        endpoints.MapPost("/Account/ExternalLogin", BeginExternalLoginAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapGet("/Account/ExternalLoginCallback", CompleteExternalLoginAsync).AllowAnonymous();
        endpoints.MapGet("/Account/Denied", () => Results.Content(RenderPage("No access", "<p>当前账号没有 Dashboard 权限。</p><p><a class=\"bocchi-button secondary\" href=\"/Account/Login\">Back to login</a></p>"), "text/html; charset=utf-8")).AllowAnonymous();

        return endpoints;
    }

    private static async Task<IResult> RenderSetupAsync(
        HttpContext context,
        HomeServerSetupService setup)
    {
        if (await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            return Results.Redirect(context.User.Identity?.IsAuthenticated == true ? "/Admin" : "/Account/Login");
        }

        return Results.Content(RenderSetupForm([]), "text/html; charset=utf-8");
    }

    private static async Task<IResult> SubmitSetupAsync(
        HttpContext context,
        HomeServerSetupService setup,
        SignInManager<BocchiUser> signInManager,
        UserManager<BocchiUser> users)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var email = form["email"].ToString().Trim();
        var displayName = form["displayName"].ToString().Trim();
        var password = form["password"].ToString();
        var confirm = form["confirmPassword"].ToString();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(email))
        {
            errors.Add("Email is required.");
        }

        if (password.Length < 8)
        {
            errors.Add("Password must be at least 8 characters.");
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            errors.Add("Password confirmation does not match.");
        }

        if (errors.Count > 0)
        {
            return Results.Content(RenderSetupForm(errors), "text/html; charset=utf-8");
        }

        var result = await setup.CreateFirstAdminAsync(email, password, displayName, context.RequestAborted).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.Content(RenderSetupForm(result.Errors.Select(x => x.Description)), "text/html; charset=utf-8");
        }

        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is not null)
        {
            await signInManager.SignInAsync(user, isPersistent: true).ConfigureAwait(false);
        }

        return Results.Redirect("/Admin");
    }

    private static async Task<IResult> RenderLoginAsync(
        [FromQuery(Name = "returnUrl")] string? returnUrl,
        [FromQuery(Name = "message")] string? message,
        ExternalLoginSettingsService external)
    {
        var providers = await external.ListReadyForLoginAsync().ConfigureAwait(false);
        return Results.Content(RenderLoginForm(returnUrl, message, providers), "text/html; charset=utf-8");
    }

    private static async Task<IResult> SubmitLoginAsync(
        HttpContext context,
        UserManager<BocchiUser> users,
        SignInManager<BocchiUser> signInManager,
        [FromQuery(Name = "returnUrl")] string? returnUrl)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var email = form["email"].ToString().Trim();
        var password = form["password"].ToString();
        var remember = string.Equals(form["remember"].ToString(), "on", StringComparison.OrdinalIgnoreCase);
        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);

        if (user is null || user.IsDisabled)
        {
            return Results.Content(RenderLoginForm(returnUrl, "账号不存在或已被禁用。", []), "text/html; charset=utf-8");
        }

        var result = await signInManager.PasswordSignInAsync(user, password, remember, lockoutOnFailure: false).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.Content(RenderLoginForm(returnUrl, "邮箱或密码不正确。", []), "text/html; charset=utf-8");
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
        SignInManager<BocchiUser> signInManager)
    {
        var ready = await settings.ListReadyForLoginAsync(context.RequestAborted).ConfigureAwait(false);
        if (!ready.Any(x => string.Equals(x.ProviderKey, provider, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Redirect("/Account/Login?message=" + Uri.EscapeDataString("该登录方式尚未完整配置。"));
        }

        var redirectUrl = "/Account/ExternalLoginCallback?returnUrl=" + Uri.EscapeDataString(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin");
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Results.Challenge(properties, [provider]);
    }

    private static async Task<IResult> CompleteExternalLoginAsync(
        [FromQuery(Name = "returnUrl")] string? returnUrl,
        SignInManager<BocchiUser> signInManager,
        UserManager<BocchiUser> users)
    {
        var info = await signInManager.GetExternalLoginInfoAsync().ConfigureAwait(false);
        if (info is null)
        {
            return Results.Redirect("/Account/Login?message=" + Uri.EscapeDataString("无法读取第三方登录结果。"));
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
            return Results.Redirect("/Account/Login?message=" + Uri.EscapeDataString("第三方账号没有返回可绑定的 email。"));
        }

        var user = await users.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null || user.IsDisabled)
        {
            return Results.Redirect("/Account/Login?message=" + Uri.EscapeDataString("第三方账号已验证，但需要先由 Admin 创建或启用对应用户。"));
        }

        var bind = await users.AddLoginAsync(user, info).ConfigureAwait(false);
        if (!bind.Succeeded && !bind.Errors.Any(x => x.Code.Contains("LoginAlreadyAssociated", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.Redirect("/Account/Login?message=" + Uri.EscapeDataString("第三方账号绑定失败，请在 Users 页面检查该账号。"));
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user).ConfigureAwait(false);
        await signInManager.SignInAsync(user, isPersistent: true, info.LoginProvider).ConfigureAwait(false);
        return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin");
    }

    private static string RenderSetupForm(IEnumerable<string> errors)
        => RenderPage("Setup", $$"""
            <form class="bocchi-auth-card" method="post" action="/Setup">
                <p class="bocchi-page-heading__eyebrow">Welcome / Setup</p>
                <h1>Start Bocchi gently.</h1>
                <p>初始化 Home Server 数据库，并创建第一个 Admin 账户。完成后公开 Setup 会关闭。</p>
                {{RenderErrors(errors)}}
                <label>Email<input name="email" type="text" inputmode="email" autocomplete="username" required></label>
                <label>Display name<input name="displayName" type="text" autocomplete="name"></label>
                <label>Password<input name="password" type="password" autocomplete="new-password" minlength="8" required></label>
                <label>Confirm password<input name="confirmPassword" type="password" autocomplete="new-password" minlength="8" required></label>
                <button type="submit">Create Admin</button>
            </form>
            """);

    private static string RenderLoginForm(
        string? returnUrl,
        string? message,
        IReadOnlyList<ExternalLoginProviderSettings> providers)
    {
        var safeReturn = WebUtility.HtmlEncode(IsLocalUrl(returnUrl) ? returnUrl : "/Admin");
        var providerButtons = string.Join("\n", providers.Select(provider => $$"""
            <form method="post" action="/Account/ExternalLogin" class="bocchi-auth-provider">
                <input type="hidden" name="provider" value="{{WebUtility.HtmlEncode(provider.ProviderKey)}}">
                <input type="hidden" name="returnUrl" value="{{safeReturn}}">
                <button type="submit" class="secondary">Continue with {{WebUtility.HtmlEncode(provider.DisplayName)}}</button>
            </form>
            """));

        return RenderPage("Login", $$"""
            <form class="bocchi-auth-card" method="post" action="/Account/Login?returnUrl={{Uri.EscapeDataString(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin")}}">
                <p class="bocchi-page-heading__eyebrow">Login</p>
                <h1>Back to your workspace.</h1>
                <p>Home Server 默认受保护；登录后进入 `/Admin`，前台 `/` 也只是受保护 Preview。</p>
                {{(string.IsNullOrWhiteSpace(message) ? string.Empty : $"<p class=\"bocchi-auth-message\">{WebUtility.HtmlEncode(message)}</p>")}}
                <label>Email<input name="email" type="text" inputmode="email" autocomplete="username" required></label>
                <label>Password<input name="password" type="password" autocomplete="current-password" required></label>
                <label class="bocchi-checkbox"><input name="remember" type="checkbox">Remember me</label>
                <button type="submit">Login</button>
            </form>
            {{providerButtons}}
            """);
    }

    private static string RenderPage(string title, string body)
        => $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{WebUtility.HtmlEncode(title)}} · Bocchi</title>
                <link rel="stylesheet" href="/app.css">
            </head>
            <body class="bocchi-auth-page">
                <main>{{body}}</main>
            </body>
            </html>
            """;

    private static string RenderErrors(IEnumerable<string> errors)
    {
        var list = errors.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (list.Count == 0)
        {
            return string.Empty;
        }

        return "<ul class=\"bocchi-auth-errors\">" + string.Join("", list.Select(x => $"<li>{WebUtility.HtmlEncode(x)}</li>")) + "</ul>";
    }

    private static bool IsLocalUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl)
            && returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal);
}
