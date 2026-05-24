using System.Security.Cryptography;
using System.Text.Json;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.DataProtection;

namespace Bocchi.HomeServer.Security;

/// <summary>Setup 第一页 Admin 表单缓冲区；错误回显时只保留非密码字段。</summary>
internal sealed record SetupAdminFormValues
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

    /// <summary>转换为第二步 Cookie 携带的 Admin 输入。</summary>
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
internal sealed record SetupSiteFormValues
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
internal sealed record PendingSetupAdmin
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

/// <summary>Setup 两步之间的短期 Cookie 存储，只保存被 Data Protection 保护后的 Admin 输入。</summary>
internal static class SetupPendingAdminCookie
{
    /// <summary>Setup pending admin Cookie 名称。</summary>
    public const string Name = "Bocchi.Setup.PendingAdmin";

    /// <summary>第一页 Admin 输入在第二页表单中携带的短期保护时长。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    /// <summary>写入短期 Cookie，供第二页提交时读取。</summary>
    public static void Write(HttpContext context, IDataProtectionProvider protection, PendingSetupAdmin pendingAdmin)
    {
        var payload = CreateProtector(protection).Protect(JsonSerializer.Serialize(pendingAdmin), Lifetime);
        context.Response.Cookies.Append(
            Name,
            payload,
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.Add(Lifetime),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
            });
    }

    /// <summary>读取并解保护 pending Admin 输入；过期或被篡改时返回 false。</summary>
    public static bool TryRead(HttpContext context, IDataProtectionProvider protection, out PendingSetupAdmin pendingAdmin)
    {
        pendingAdmin = new PendingSetupAdmin();
        if (!context.Request.Cookies.TryGetValue(Name, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var json = CreateProtector(protection).Unprotect(payload);
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

    /// <summary>清理 Setup pending Admin Cookie。</summary>
    public static void Clear(HttpContext context)
    {
        context.Response.Cookies.Delete(Name);
    }

    /// <summary>创建 Setup 专用的限时 Data Protection protector。</summary>
    private static ITimeLimitedDataProtector CreateProtector(IDataProtectionProvider protection)
        => protection
            .CreateProtector("Bocchi.HomeServer.Setup.PendingAdmin")
            .ToTimeLimitedDataProtector();
}
