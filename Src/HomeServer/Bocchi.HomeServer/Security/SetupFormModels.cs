using System.Security.Cryptography;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

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
    public string DefaultThemeId { get; init; } = "bocchi-mono";

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

/// <summary>Setup 两步之间的短期服务端状态；Cookie 只保存限时保护后的随机句柄，不携带密码 payload。</summary>
internal sealed class SetupPendingAdminStore
{
    /// <summary>Setup pending admin Cookie 名称。</summary>
    public const string Name = "Bocchi.Setup.PendingAdmin";

    /// <summary>Setup pending admin Data Protection purpose；测试用它确认 Cookie payload 不再是 Admin JSON。</summary>
    public const string ProtectorPurpose = "Bocchi.HomeServer.Setup.PendingAdmin";

    /// <summary>第一页 Admin 输入在第二页表单中携带的短期保护时长。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    private const string CacheKeyPrefix = "Bocchi.HomeServer.Setup.PendingAdminStore:";

    private readonly IMemoryCache _cache;
    private readonly ITimeLimitedDataProtector _protector;

    /// <summary>构造 Setup pending admin 服务端短期状态。</summary>
    public SetupPendingAdminStore(IMemoryCache cache, IDataProtectionProvider protection)
    {
        _cache = cache;
        _protector = protection
            .CreateProtector(ProtectorPurpose)
            .ToTimeLimitedDataProtector();
    }

    /// <summary>写入短期 Cookie，供第二页提交时读取。</summary>
    public void Write(HttpContext context, PendingSetupAdmin pendingAdmin)
    {
        var cacheKey = CacheKeyPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _cache.Set(
            cacheKey,
            pendingAdmin,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Lifetime,
            });
        var payload = _protector.Protect(cacheKey, Lifetime);
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
    public bool TryRead(HttpContext context, out PendingSetupAdmin pendingAdmin)
    {
        pendingAdmin = new PendingSetupAdmin();
        if (!TryReadCacheKey(context, out var cacheKey))
        {
            return false;
        }

        if (!_cache.TryGetValue(cacheKey, out PendingSetupAdmin? cached) || cached is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cached.UserName) || string.IsNullOrWhiteSpace(cached.Password))
        {
            return false;
        }

        pendingAdmin = cached;
        return true;
    }

    /// <summary>清理 Setup pending Admin Cookie。</summary>
    public void Clear(HttpContext context)
    {
        if (TryReadCacheKey(context, out var cacheKey))
        {
            _cache.Remove(cacheKey);
        }

        context.Response.Cookies.Delete(Name);
    }

    /// <summary>从 Cookie 中还原服务端 cache key；失败时说明 Cookie 已过期或被篡改。</summary>
    private bool TryReadCacheKey(HttpContext context, out string cacheKey)
    {
        cacheKey = string.Empty;
        if (!context.Request.Cookies.TryGetValue(Name, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            cacheKey = _protector.Unprotect(payload);
            return cacheKey.StartsWith(CacheKeyPrefix, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
