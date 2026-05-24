using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Bocchi.HomeServer.Security;

/// <summary>
/// Setup、Login、Logout 与外部登录回调端点。Cookie 写入必须走普通 HTTP 端点，不放进 interactive component。
/// </summary>
public static class AccountEndpoints
{
    /// <summary>第一页 Admin 输入在第二页表单中携带的短期保护时长。</summary>
    private static readonly TimeSpan SetupPayloadLifetime = TimeSpan.FromMinutes(20);

    /// <summary>注册账户与 Setup 相关端点。</summary>
    public static IEndpointRouteBuilder MapBocchiAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/Setup", RenderSetupAsync).AllowAnonymous();
        endpoints.MapGet("/Setup/Site", () => Results.Redirect("/Setup")).AllowAnonymous();
        endpoints.MapPost("/Setup/Site", SubmitSetupAdminAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapGet("/Setup/Complete", () => Results.Redirect("/Setup")).AllowAnonymous();
        endpoints.MapPost("/Setup/Complete", CompleteSetupAsync).AllowAnonymous().DisableAntiforgery();
        endpoints.MapPost("/Setup/UiLanguage", SetSetupUiLanguage).AllowAnonymous().DisableAntiforgery();
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
        HomeServerSetupService setup,
        DashboardLocalizationService localization)
    {
        if (await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            return Results.Redirect(context.User.Identity?.IsAuthenticated == true ? "/Admin" : "/Account/Login");
        }

        return Results.Content(RenderSetupAdminForm([], new SetupAdminFormValues(), localization), "text/html; charset=utf-8");
    }

    private static async Task<IResult> SubmitSetupAdminAsync(
        HttpContext context,
        HomeServerSetupService setup,
        SiteProfileSettingsService siteProfile,
        ThemeSettingsService themeSettings,
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
            return Results.Content(RenderSetupAdminForm(errors, values, localization), "text/html; charset=utf-8");
        }

        var site = await siteProfile.GetAsync(context.RequestAborted).ConfigureAwait(false);
        var siteValues = SetupSiteFormValues.FromSite(site);
        var themeOptions = await themeSettings.ListAvailableThemesAsync(context.RequestAborted).ConfigureAwait(false);
        var setupPayload = ProtectPendingAdmin(protection, values.ToPendingAdmin());
        return Results.Content(RenderSetupSiteForm([], siteValues, setupPayload, themeOptions, localization), "text/html; charset=utf-8");
    }

    private static async Task<IResult> CompleteSetupAsync(
        HttpContext context,
        HomeServerSetupService setup,
        SignInManager<BocchiUser> signInManager,
        UserManager<BocchiUser> users,
        ThemeSettingsService themeSettings,
        IDataProtectionProvider protection,
        DashboardLocalizationService localization)
    {
        if (await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            return Results.Redirect(context.User.Identity?.IsAuthenticated == true ? "/Admin" : "/Account/Login");
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        var values = SetupSiteFormValues.FromForm(form);
        if (!TryUnprotectPendingAdmin(protection, form["setupPayload"].ToString(), out var pendingAdmin))
        {
            return Results.Content(
                RenderSetupAdminForm([localization["setup.error.payloadExpired"]], new SetupAdminFormValues(), localization),
                "text/html; charset=utf-8");
        }

        var errors = ValidateSite(values, localization);

        if (errors.Count > 0)
        {
            var setupPayload = ProtectPendingAdmin(protection, pendingAdmin);
            var themeOptions = await themeSettings.ListAvailableThemesAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Content(RenderSetupSiteForm(errors, values, setupPayload, themeOptions, localization), "text/html; charset=utf-8");
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
            return Results.Content(
                RenderSetupAdminForm(result.Errors.Select(x => x.Description), SetupAdminFormValues.FromPendingAdmin(pendingAdmin), localization),
                "text/html; charset=utf-8");
        }

        var user = await users.FindByNameAsync(pendingAdmin.UserName).ConfigureAwait(false);
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
        var userName = form["username"].ToString().Trim();
        var password = form["password"].ToString();
        var remember = string.Equals(form["remember"].ToString(), "on", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Content(RenderLoginForm(returnUrl, "用户名或密码不正确。", []), "text/html; charset=utf-8");
        }

        var user = await users.FindByNameAsync(userName).ConfigureAwait(false);

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

    private static IResult SetSetupUiLanguage(
        HttpContext context,
        [FromForm] string uiLanguage,
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

        return Results.LocalRedirect("/Setup");
    }

    /// <summary>渲染 Setup 第一页：只收集第一个 Admin 账号。</summary>
    private static string RenderSetupAdminForm(
        IEnumerable<string> errors,
        SetupAdminFormValues values,
        DashboardLocalizationService localization)
        => RenderSetupPage(localization, $$"""
            <form class="bocchi-setup-form" method="post" action="/Setup/Site">
                <header class="bocchi-setup-hero">
                    <p class="bocchi-page-heading__eyebrow">{{Html(localization["setup.page.eyebrow"])}}</p>
                    <h1 id="setup-title">{{Html(localization["setup.page.heading"])}}</h1>
                    <p>{{Html(localization["setup.page.description"])}}</p>
                </header>

                {{RenderErrors(errors)}}

                <section class="bocchi-setup-step" aria-labelledby="setup-admin-title">
                    <div class="bocchi-setup-step__marker" aria-hidden="true">1</div>
                    <div class="bocchi-setup-step__body">
                        <div class="bocchi-section-heading">
                            <p>{{Html(localization["setup.admin.eyebrow"])}}</p>
                            <h2 id="setup-admin-title">{{Html(localization["setup.admin.title"])}}</h2>
                        </div>
                        <div class="bocchi-setup-field-stack bocchi-setup-field-stack--admin">
                            <div class="bocchi-setup-field-row bocchi-setup-field-row--compact">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("user")}}</span>
                                <label>{{Html(SetupText(localization, "setup.field.username", "用户名", "Username"))}}
                                    <input name="username" type="text" autocomplete="username" value="{{Attr(values.UserName)}}" placeholder="owner" required>
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row bocchi-setup-field-row--compact">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("user")}}</span>
                                <label>{{Html(localization["setup.field.displayName"])}}
                                    <input name="displayName" type="text" autocomplete="name" value="{{Attr(values.DisplayName)}}" placeholder="{{Attr(localization["setup.field.displayName.placeholder"])}}">
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row bocchi-setup-field-row--compact bocchi-setup-field-row--wide">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("mail")}}</span>
                                <label>{{Html(SetupText(localization, "setup.field.emailOptional", "Email（可选）", "Email (optional)"))}}
                                    <input name="email" type="email" inputmode="email" autocomplete="email" value="{{Attr(values.Email)}}" placeholder="name@example.com">
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row bocchi-setup-field-row--compact">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("lock")}}</span>
                                <label>{{Html(localization["setup.field.password"])}}
                                    <span class="bocchi-password-input">
                                        <input id="setup-password" name="password" type="password" autocomplete="new-password" minlength="8" data-bocchi-password-source required>
                                        <button class="bocchi-password-toggle" type="button" data-bocchi-password-toggle="setup-password" aria-label="{{Attr(SetupText(localization, "setup.password.toggle", "显示或隐藏密码", "Show or hide password"))}}" title="{{Attr(SetupText(localization, "setup.password.toggle", "显示或隐藏密码", "Show or hide password"))}}">{{Icon("eye")}}</button>
                                    </span>
                                </label>
                                <div class="bocchi-password-meter" data-strength="weak" data-bocchi-password-meter data-label-weak="{{Attr(SetupText(localization, "setup.passwordStrength.weak", "弱", "Weak"))}}" data-label-medium="{{Attr(SetupText(localization, "setup.passwordStrength.medium", "中", "Medium"))}}" data-label-strong="{{Attr(SetupText(localization, "setup.passwordStrength.strong", "强", "Strong"))}}">
                                    <span>{{Html(SetupText(localization, "setup.passwordStrength.label", "密码强度:", "Password strength:"))}} <strong data-bocchi-password-strength-label>{{Html(SetupText(localization, "setup.passwordStrength.weak", "弱", "Weak"))}}</strong></span>
                                    <div aria-hidden="true"><i></i><i></i><i></i></div>
                                </div>
                            </div>
                            <div class="bocchi-setup-field-row bocchi-setup-field-row--compact">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("lock")}}</span>
                                <label>{{Html(localization["setup.field.confirmPassword"])}}
                                    <span class="bocchi-password-input">
                                        <input id="setup-confirm-password" name="confirmPassword" type="password" autocomplete="new-password" minlength="8" data-bocchi-password-confirm required>
                                        <button class="bocchi-password-toggle" type="button" data-bocchi-password-toggle="setup-confirm-password" aria-label="{{Attr(SetupText(localization, "setup.password.toggle", "显示或隐藏密码", "Show or hide password"))}}" title="{{Attr(SetupText(localization, "setup.password.toggle", "显示或隐藏密码", "Show or hide password"))}}">{{Icon("eye")}}</button>
                                    </span>
                                </label>
                            </div>
                        </div>
                        <ul class="bocchi-setup-checklist bocchi-setup-checklist--panel" aria-label="{{Attr(localization["setup.passwordChecklist.aria"])}}">
                            <li data-ok="false" data-bocchi-password-rule="length">{{Icon("check")}}{{Html(localization["setup.passwordChecklist.length"])}}</li>
                            <li data-ok="false" data-bocchi-password-rule="letter">{{Icon("check")}}{{Html(SetupText(localization, "setup.passwordChecklist.letter", "包含字母。", "Includes a letter."))}}</li>
                            <li data-ok="false" data-bocchi-password-rule="numberOrSymbol">{{Icon("check")}}{{Html(SetupText(localization, "setup.passwordChecklist.numberOrSymbol", "包含数字或特殊字符。", "Includes a number or symbol."))}}</li>
                            <li data-ok="false" data-bocchi-password-rule="match">{{Icon("check")}}{{Html(SetupText(localization, "setup.passwordChecklist.match", "两次输入一致。", "Both entries match."))}}</li>
                        </ul>
                    </div>
                </section>

                <div class="bocchi-setup-actions">
                    <button class="bocchi-setup-submit" type="submit">{{Html(SetupText(localization, "setup.action.next", "下一步", "Next", preferFallback: true))}}</button>
                </div>
                <p class="bocchi-setup-footnote">{{Icon("lock")}}{{Html(SetupText(localization, "setup.action.payloadNote", "请妥善保管管理员账号信息。", "Keep the Admin account details safe.", preferFallback: true))}}</p>
            </form>
            """);

    /// <summary>渲染 Setup 第二页：只收集 Home Server 必须知道的站点基础约定。</summary>
    private static string RenderSetupSiteForm(
        IEnumerable<string> errors,
        SetupSiteFormValues values,
        string setupPayload,
        IReadOnlyList<ThemeOption> themeOptions,
        DashboardLocalizationService localization)
        => RenderSetupPage(localization, $$"""
            <form class="bocchi-setup-form" method="post" action="/Setup/Complete">
                <input type="hidden" name="setupPayload" value="{{Attr(setupPayload)}}">
                <header class="bocchi-setup-hero">
                    <p class="bocchi-page-heading__eyebrow">{{Html(localization["setup.page.eyebrow"])}}</p>
                    <h1 id="setup-title">{{Html(localization["setup.page.heading"])}}</h1>
                    <p>{{Html(localization["setup.page.description"])}}</p>
                </header>

                {{RenderErrors(errors)}}

                <section class="bocchi-setup-step" aria-labelledby="setup-site-title">
                    <div class="bocchi-setup-step__marker" aria-hidden="true">2</div>
                    <div class="bocchi-setup-step__body">
                        <div class="bocchi-section-heading">
                            <p>{{Html(localization["setup.site.eyebrow"])}}</p>
                            <h2 id="setup-site-title">{{Html(localization["setup.site.title"])}}</h2>
                        </div>
                        <div class="bocchi-setup-field-grid bocchi-setup-field-grid--site">
                            <div class="bocchi-setup-field-row">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("sparkles")}}</span>
                                <label>{{Html(localization["setup.field.siteName"])}}
                                    <input name="siteName" type="text" value="{{Attr(values.SiteName)}}" placeholder="Bocchi" required>
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("type")}}</span>
                                <label>{{Html(localization["setup.field.defaultTitle"])}}
                                    <input name="defaultTitle" type="text" value="{{Attr(values.DefaultTitle)}}" placeholder="{{Attr(localization["setup.field.defaultTitle.placeholder"])}}">
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("link")}}</span>
                                <label>{{Html(localization["setup.field.publicBaseUrl"])}}
                                    <input name="publicBaseUrl" type="url" value="{{Attr(values.PublicBaseUrl)}}" placeholder="{{Attr(SetupText(localization, "setup.field.publicBaseUrl.placeholder", "https://domain.com/", "https://domain.com/"))}}">
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("palette")}}</span>
                                <label>{{Html(localization["setup.field.defaultThemeId"])}}
                                    <select name="defaultThemeId" required>
                                        {{RenderThemeOptions(themeOptions, values.DefaultThemeId)}}
                                    </select>
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row bocchi-setup-field-grid__wide">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("file-text")}}</span>
                                <label>{{Html(localization["setup.field.description"])}}
                                    <input name="description" type="text" value="{{Attr(values.Description)}}" placeholder="{{Attr(localization["setup.field.description.placeholder"])}}">
                                </label>
                            </div>
                            <div class="bocchi-setup-field-row bocchi-setup-field-grid__wide">
                                <span class="bocchi-setup-field-icon" aria-hidden="true">{{Icon("copyright")}}</span>
                                <label>{{Html(localization["setup.field.copyright"])}}
                                    <input name="copyrightNotice" type="text" value="{{Attr(values.CopyrightNotice)}}" placeholder="{{Attr(localization["setup.field.copyright.placeholder"])}}">
                                </label>
                            </div>
                        </div>
                        <p class="bocchi-setup-note">{{Icon("lock")}}{{Html(localization["setup.site.note"])}}</p>
                    </div>
                </section>

                <div class="bocchi-setup-actions">
                    <a class="bocchi-button secondary" href="/Setup">{{Html(localization["setup.action.back"])}}</a>
                    <button class="bocchi-setup-submit" type="submit">{{Html(localization["setup.action.finish"])}}</button>
                </div>
                <p class="bocchi-setup-footnote">{{Icon("lock")}}{{Html(localization["setup.action.note"])}}</p>
            </form>
            """);

    /// <summary>渲染 Setup 两页共用的顶栏、辅助说明和页面骨架。</summary>
    private static string RenderSetupPage(DashboardLocalizationService localization, string form)
        => RenderPage(localization["setup.page.title"], $$"""
            <div class="bocchi-auth-topbar">
                <a class="bocchi-auth-logo" href="/Setup" aria-label="Bocchi Setup">Bocchi</a>
                <div class="bocchi-auth-topbar__actions">
                    <form class="bocchi-auth-language" method="post" action="/Setup/UiLanguage">
                        <label>
                            {{Icon("globe")}}
                            <select name="uiLanguage" aria-label="{{Attr(localization["setup.language.aria"])}}" onchange="this.form.submit()">
                                {{RenderLanguageOptions(localization)}}
                            </select>
                        </label>
                    </form>
                    <button
                        class="bocchi-auth-icon-button"
                        type="button"
                        data-bocchi-appearance-toggle
                        data-label-dark="{{Attr(localization["setup.appearance.switchToDark"])}}"
                        data-label-light="{{Attr(localization["setup.appearance.switchToLight"])}}"
                        aria-label="{{Attr(localization["setup.appearance.switchToDark"])}}"
                        title="{{Attr(localization["setup.appearance.switchToDark"])}}">
                        {{Icon("moon-sun")}}
                    </button>
                </div>
            </div>

            <section class="bocchi-setup-layout" aria-labelledby="setup-title">
                {{form}}
                {{RenderSetupHelper(localization)}}
            </section>
            """, localization.CurrentLanguage.Code);

    /// <summary>渲染 Setup 右侧说明，明确数据库权威与 workspace 投影的关系。</summary>
    private static string RenderSetupHelper(DashboardLocalizationService localization)
        => $$"""
            <aside class="bocchi-setup-helper" aria-label="{{Attr(localization["setup.helper.aria"])}}">
                <span class="bocchi-setup-helper__icon" aria-hidden="true">{{Icon("sparkles")}}</span>
                <div>
                    <h2>{{Html(SetupText(localization, "setup.helper.title", "首次运行", "First run", preferFallback: true))}}</h2>
                    <p>{{Html(SetupText(localization, "setup.helper.description", "这是 Bocchi Home Server 的首次运行。完成设置后，系统会初始化必要数据。", "This is the first run of Bocchi Home Server. Setup initializes the required data.", preferFallback: true))}}</p>
                </div>
            </aside>
            """;

    /// <summary>校验第一页 Admin 输入；Identity 的最终校验仍在创建账号时执行。</summary>
    private static List<string> ValidateAdmin(SetupAdminFormValues values, DashboardLocalizationService localization)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(values.UserName))
        {
            errors.Add(SetupText(localization, "setup.error.usernameRequired", "用户名必填。", "Username is required."));
        }

        if (!string.IsNullOrWhiteSpace(values.Email) && !IsValidEmail(values.Email))
        {
            errors.Add(SetupText(localization, "setup.error.emailInvalid", "Email 格式不正确。", "Email is not valid."));
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

    /// <summary>创建 Setup 专用的限时 Data Protection protector。</summary>
    private static ITimeLimitedDataProtector CreateSetupProtector(IDataProtectionProvider protection)
        => protection
            .CreateProtector("Bocchi.HomeServer.Setup.PendingAdmin")
            .ToTimeLimitedDataProtector();

    /// <summary>把第一页 Admin 输入保护后交给第二页隐藏字段携带。</summary>
    private static string ProtectPendingAdmin(IDataProtectionProvider protection, PendingSetupAdmin pendingAdmin)
        => CreateSetupProtector(protection).Protect(JsonSerializer.Serialize(pendingAdmin), SetupPayloadLifetime);

    /// <summary>读取第二页提交的 Admin 输入；过期或被篡改时要求用户回到第一页重填。</summary>
    private static bool TryUnprotectPendingAdmin(
        IDataProtectionProvider protection,
        string payload,
        out PendingSetupAdmin pendingAdmin)
    {
        pendingAdmin = new PendingSetupAdmin();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var json = CreateSetupProtector(protection).Unprotect(payload);
            var parsed = JsonSerializer.Deserialize<PendingSetupAdmin>(json);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.UserName) || string.IsNullOrWhiteSpace(parsed.Password))
            {
                return false;
            }

            pendingAdmin = parsed;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Setup 是普通 endpoint；这里给刚新增的文案提供 fallback，避免 dotnet watch 热重载时旧 singleton 资源未刷新。</summary>
    private static string SetupText(
        DashboardLocalizationService localization,
        string key,
        string zhCnFallback,
        string enUsFallback,
        bool preferFallback = false)
    {
        var fallback = string.Equals(localization.CurrentLanguage.Code, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? zhCnFallback
            : enUsFallback;
        if (preferFallback)
        {
            return fallback;
        }

        var value = localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static string RenderLanguageOptions(DashboardLocalizationService localization)
        => string.Join("", DashboardLocalizationService.SupportedDashboardLanguages.Select(language =>
        {
            var selected = string.Equals(localization.CurrentLanguage.Code, language.Code, StringComparison.OrdinalIgnoreCase)
                ? " selected"
                : string.Empty;
            return $"<option value=\"{Attr(language.Code)}\"{selected}>{Html(language.NativeName)}</option>";
        }));

    /// <summary>渲染 Setup 第二页的前台 Theme 下拉选项，并保留当前已选 Theme。</summary>
    private static string RenderThemeOptions(IReadOnlyList<ThemeOption> themeOptions, string selectedThemeId)
    {
        var normalizedSelected = string.IsNullOrWhiteSpace(selectedThemeId) ? "default-static" : selectedThemeId.Trim();
        var options = themeOptions.Count > 0
            ? themeOptions
            : [new ThemeOption("default-static", "Bocchi Mono")];
        var hasSelected = options.Any(option => string.Equals(option.Id, normalizedSelected, StringComparison.Ordinal));
        var rendered = options.Select(option =>
        {
            var selected = string.Equals(option.Id, normalizedSelected, StringComparison.Ordinal) ? " selected" : string.Empty;
            return $"<option value=\"{Attr(option.Id)}\"{selected}>{Html(option.Name)} ({Html(option.Id)})</option>";
        });

        if (hasSelected)
        {
            return string.Join("", rendered);
        }

        var current = $"<option value=\"{Attr(normalizedSelected)}\" selected>{Html(normalizedSelected)}</option>";
        return current + string.Join("", rendered);
    }

    private static bool IsPublicBaseUrl(string value)
        => string.IsNullOrWhiteSpace(value) ||
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";

    private static bool IsValidEmail(string value)
        => value.Count(c => c == '@') == 1
            && value.IndexOf('@') > 0
            && value.IndexOf('@') < value.Length - 1;

    private static string Icon(string name)
        => name switch
        {
            "check" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>
                """,
            "database" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M3 5v14c0 1.7 4 3 9 3s9-1.3 9-3V5"/><path d="M3 12c0 1.7 4 3 9 3s9-1.3 9-3"/></svg>
                """,
            "globe" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M2 12h20"/><path d="M12 2a15.3 15.3 0 0 1 0 20"/><path d="M12 2a15.3 15.3 0 0 0 0 20"/></svg>
                """,
            "lock" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="18" height="11" x="3" y="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                """,
            "copyright" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M14.8 9.5a4 4 0 1 0 0 5"/></svg>
                """,
            "eye" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2.1 12s3.6-7 9.9-7 9.9 7 9.9 7-3.6 7-9.9 7-9.9-7-9.9-7Z"/><circle cx="12" cy="12" r="3"/></svg>
                """,
            "file-text" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><path d="M10 9H8"/><path d="M16 13H8"/><path d="M16 17H8"/></svg>
                """,
            "link" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13a5 5 0 0 0 7.1 0l2-2a5 5 0 0 0-7.1-7.1l-1.1 1.1"/><path d="M14 11a5 5 0 0 0-7.1 0l-2 2A5 5 0 0 0 12 20.1l1.1-1.1"/></svg>
                """,
            "mail" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect width="20" height="16" x="2" y="4" rx="2"/><path d="m22 7-8.9 5.7a2 2 0 0 1-2.2 0L2 7"/></svg>
                """,
            "moon-sun" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <g class="bocchi-icon-moon"><path d="M12 3a6 6 0 0 0 9 7.5A9 9 0 1 1 12 3Z"/></g>
                    <g class="bocchi-icon-sun"><circle cx="12" cy="12" r="4"/><path d="M12 2v2"/><path d="M12 20v2"/><path d="m4.93 4.93 1.41 1.41"/><path d="m17.66 17.66 1.41 1.41"/><path d="M2 12h2"/><path d="M20 12h2"/><path d="m6.34 17.66-1.41 1.41"/><path d="m19.07 4.93-1.41 1.41"/></g>
                </svg>
                """,
            "palette" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="13.5" cy="6.5" r=".5" fill="currentColor"/><circle cx="17.5" cy="10.5" r=".5" fill="currentColor"/><circle cx="8.5" cy="7.5" r=".5" fill="currentColor"/><circle cx="6.5" cy="12.5" r=".5" fill="currentColor"/><path d="M12 2a10 10 0 0 0 0 20 1.7 1.7 0 0 0 1.3-2.8 1.7 1.7 0 0 1 1.3-2.8h1.8A5.6 5.6 0 0 0 22 10.8C22 5.9 17.5 2 12 2Z"/></svg>
                """,
            "shield" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.7 8.8a1 1 0 0 1-.6 0C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.2-2.7a1.2 1.2 0 0 1 1.6 0C14.5 3.8 17 5 19 5a1 1 0 0 1 1 1z"/></svg>
                """,
            "sparkles" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9.9 3.2 8.2 8.3 3.1 10l5.1 1.7 1.7 5.1 1.7-5.1 5.1-1.7-5.1-1.7Z"/><path d="M18.5 13.5 17.7 16l-2.4.8 2.4.8.8 2.4.8-2.4 2.4-.8-2.4-.8Z"/></svg>
                """,
            "type" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7V4h16v3"/><path d="M9 20h6"/><path d="M12 4v16"/></svg>
                """,
            "user" => """
                <svg aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21a8 8 0 0 0-16 0"/><circle cx="12" cy="7" r="4"/></svg>
                """,
            _ => string.Empty,
        };

    private static string Html(string value)
        => WebUtility.HtmlEncode(value);

    private static string Attr(string value)
        => WebUtility.HtmlEncode(value);

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

        return RenderPage("登录", $$"""
            <form class="bocchi-auth-card" method="post" action="/Account/Login?returnUrl={{Uri.EscapeDataString(IsLocalUrl(returnUrl) ? returnUrl! : "/Admin")}}">
                <p class="bocchi-eyebrow">登录</p>
                <h1>欢迎回到 Bocchi</h1>
                <p>Home Server 默认受保护；登录后进入 <code>/Admin</code>，前台 <code>/</code> 也是受保护的 Preview。</p>
                {{(string.IsNullOrWhiteSpace(message) ? string.Empty : $"<p class=\"bocchi-auth-message\">{WebUtility.HtmlEncode(message)}</p>")}}
                <label>用户名<input name="username" type="text" autocomplete="username" required></label>
                <label>密码<input name="password" type="password" autocomplete="current-password" required></label>
                <label class="bocchi-checkbox"><input name="remember" type="checkbox">记住我</label>
                <button type="submit">登录</button>
            </form>
            {{providerButtons}}
            """);
    }

    private static string RenderPage(string title, string body, string languageCode = "zh-CN")
        => $$"""
            <!doctype html>
            <html lang="{{WebUtility.HtmlEncode(languageCode)}}">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{WebUtility.HtmlEncode(title)}} · Bocchi</title>
                <link rel="icon" href="/favicon.ico" sizes="any">
                <link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png">
                <link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png">
                <link rel="icon" type="image/png" sizes="48x48" href="/favicon-48x48.png">
                <link rel="apple-touch-icon" sizes="180x180" href="/apple-touch-icon.png">
                <link rel="manifest" href="/site.webmanifest">
                <meta name="theme-color" content="#f8f7fc">
                <link rel="stylesheet" href="/app.css">
                <script src="/bocchi-appearance.js"></script>
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

    /// <summary>Setup 第一页 Admin 表单缓冲区；错误回显时只保留非密码字段。</summary>
    private sealed record SetupAdminFormValues
    {
        /// <summary>Admin 用户名，作为本地账号登录主标识。</summary>
        public string UserName { get; init; } = string.Empty;

        /// <summary>Admin 可选 email，只作为联系信息和外部登录辅助绑定。</summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>Admin 显示名。</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Admin 密码，只用于当前 Setup 流程，不回显到页面。</summary>
        public string Password { get; init; } = string.Empty;

        /// <summary>确认密码，只用于第一页校验。</summary>
        public string ConfirmPassword { get; init; } = string.Empty;

        /// <summary>从 HTTP form 读取第一页 Admin 输入。</summary>
        public static SetupAdminFormValues FromForm(IFormCollection form)
            => new()
            {
                UserName = form["username"].ToString().Trim(),
                Email = form["email"].ToString().Trim(),
                DisplayName = form["displayName"].ToString().Trim(),
                Password = form["password"].ToString(),
                ConfirmPassword = form["confirmPassword"].ToString(),
            };

        /// <summary>从已保护 payload 还原非密码回显字段。</summary>
        public static SetupAdminFormValues FromPendingAdmin(PendingSetupAdmin pendingAdmin)
            => new()
            {
                UserName = pendingAdmin.UserName,
                Email = pendingAdmin.Email,
                DisplayName = pendingAdmin.DisplayName,
            };

        /// <summary>转换为第二页隐藏字段携带的 Admin 输入。</summary>
        public PendingSetupAdmin ToPendingAdmin()
            => new()
            {
                UserName = UserName,
                Email = Email,
                DisplayName = DisplayName,
                Password = Password,
            };
    }

    /// <summary>Setup 第二页站点基础约定表单缓冲区。</summary>
    private sealed record SetupSiteFormValues
    {
        /// <summary>站点名称。</summary>
        public string SiteName { get; init; } = "Bocchi";

        /// <summary>默认前台标题。</summary>
        public string DefaultTitle { get; init; } = "Bocchi";

        /// <summary>站点描述。</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>公开前台 URL。</summary>
        public string PublicBaseUrl { get; init; } = string.Empty;

        /// <summary>版权文案。</summary>
        public string CopyrightNotice { get; init; } = string.Empty;

        /// <summary>默认前台 Theme id。</summary>
        public string DefaultThemeId { get; init; } = "default-static";

        /// <summary>用现有站点配置构造第二页默认值。</summary>
        public static SetupSiteFormValues FromSite(SiteProfileSettings site)
        {
            var publicBaseUrl = string.Equals(site.PublicBaseUrl, "https://example.com/", StringComparison.Ordinal)
                ? string.Empty
                : site.PublicBaseUrl;
            return new SetupSiteFormValues
            {
                SiteName = site.SiteName,
                DefaultTitle = site.DefaultTitle,
                Description = IsOldDescriptionPlaceholder(site.Description) ? string.Empty : site.Description,
                PublicBaseUrl = publicBaseUrl,
                CopyrightNotice = site.CopyrightNotice,
                DefaultThemeId = site.DefaultThemeId,
            };
        }

        /// <summary>从 HTTP form 读取第二页站点基础约定输入。</summary>
        public static SetupSiteFormValues FromForm(IFormCollection form)
            => new()
            {
                SiteName = form["siteName"].ToString().Trim(),
                DefaultTitle = form["defaultTitle"].ToString().Trim(),
                Description = form["description"].ToString().Trim(),
                PublicBaseUrl = form["publicBaseUrl"].ToString().Trim(),
                CopyrightNotice = form["copyrightNotice"].ToString().Trim(),
                DefaultThemeId = form["defaultThemeId"].ToString().Trim(),
            };

        /// <summary>转换为站点基础设置保存输入。</summary>
        public SiteProfileSettingsUpdate ToSiteProfileUpdate()
            => new()
            {
                SiteName = SiteName,
                DefaultTitle = DefaultTitle,
                Description = Description,
                PublicBaseUrl = PublicBaseUrl,
                CopyrightNotice = CopyrightNotice,
                Language = LocalizationSettingsService.DefaultPrimaryLanguage,
                TimeZone = "Asia/Shanghai",
                DefaultThemeId = DefaultThemeId,
            };

        private static bool IsOldDescriptionPlaceholder(string value)
            => string.Equals(value.Trim(), "Personal publishing workspace", StringComparison.Ordinal);
    }

    /// <summary>第一页到第二页之间暂存的 Admin 输入，使用 Data Protection 限时保护。</summary>
    private sealed record PendingSetupAdmin
    {
        /// <summary>Admin 用户名。</summary>
        public string UserName { get; init; } = string.Empty;

        /// <summary>Admin 可选 email。</summary>
        public string Email { get; init; } = string.Empty;

        /// <summary>Admin 显示名。</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Admin 密码，只存在于当前 Setup payload 中。</summary>
        public string Password { get; init; } = string.Empty;
    }
}
