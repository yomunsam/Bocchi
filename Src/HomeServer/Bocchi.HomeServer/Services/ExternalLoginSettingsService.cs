using Bocchi.HomeServer.Data;

using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 第三方登录 Provider 设置服务，集中处理 secret 的保护与登录页按钮可见性。
/// </summary>
public sealed class ExternalLoginSettingsService
{
    private readonly BocchiDbContext _db;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly IOptionsMonitorCache<OAuthOptions> _oauthCache;
    private readonly IOptionsMonitorCache<OpenIdConnectOptions> _oidcCache;

    /// <summary>构造第三方登录设置服务。</summary>
    public ExternalLoginSettingsService(
        BocchiDbContext db,
        IDataProtectionProvider protection,
        TimeProvider time,
        IOptionsMonitorCache<OAuthOptions> oauthCache,
        IOptionsMonitorCache<OpenIdConnectOptions> oidcCache)
    {
        _db = db;
        _protector = protection.CreateProtector("Bocchi.HomeServer.ExternalLoginProviderSettings.v1");
        _time = time;
        _oauthCache = oauthCache;
        _oidcCache = oidcCache;
    }

    /// <summary>列出全部 Provider 设置。</summary>
    public async Task<IReadOnlyList<ExternalLoginProviderSettings>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.ExternalLoginProviders
            .AsNoTracking()
            .OrderBy(x => x.ProviderKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>列出登录页可显示的 Provider。</summary>
    public async Task<IReadOnlyList<ExternalLoginProviderSettings>> ListReadyForLoginAsync(CancellationToken cancellationToken = default)
        => (await ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(x => x.IsReadyForLogin())
            .ToList();

    /// <summary>保存 GitHub Provider 设置。</summary>
    public Task SaveGitHubAsync(
        bool enabled,
        string displayName,
        string? clientId,
        string? clientSecret,
        string? callbackPath,
        CancellationToken cancellationToken = default)
        => SaveAsync("github", provider =>
        {
            provider.Enabled = enabled;
            provider.DisplayName = string.IsNullOrWhiteSpace(displayName) ? "GitHub" : displayName.Trim();
            provider.ClientId = TrimToNull(clientId);
            SetSecretIfProvided(provider, clientSecret);
            provider.CallbackPath = string.IsNullOrWhiteSpace(callbackPath) ? "/signin-github" : callbackPath.Trim();
        }, cancellationToken);

    /// <summary>保存通用 OpenID Connect Provider 设置。</summary>
    public Task SaveOidcAsync(
        bool enabled,
        string displayName,
        string? authority,
        string? clientId,
        string? clientSecret,
        string? callbackPath,
        string? scopes,
        CancellationToken cancellationToken = default)
        => SaveAsync("oidc", provider =>
        {
            provider.Enabled = enabled;
            provider.DisplayName = string.IsNullOrWhiteSpace(displayName) ? "OpenID Connect" : displayName.Trim();
            provider.Authority = TrimToNull(authority);
            provider.ClientId = TrimToNull(clientId);
            SetSecretIfProvided(provider, clientSecret);
            provider.CallbackPath = string.IsNullOrWhiteSpace(callbackPath) ? "/signin-oidc-custom" : callbackPath.Trim();
            provider.ResponseType = "code";
            provider.UsePkce = true;
            provider.Scopes = string.IsNullOrWhiteSpace(scopes) ? "openid profile email" : scopes.Trim();
        }, cancellationToken);

    private async Task SaveAsync(string providerKey, Action<ExternalLoginProviderSettings> mutate, CancellationToken cancellationToken)
    {
        var provider = await _db.ExternalLoginProviders
            .FirstOrDefaultAsync(x => x.ProviderKey == providerKey, cancellationToken)
            .ConfigureAwait(false);

        if (provider is null)
        {
            provider = new ExternalLoginProviderSettings { ProviderKey = providerKey };
            _db.ExternalLoginProviders.Add(provider);
        }

        mutate(provider);
        provider.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _oauthCache.TryRemove("github");
        _oidcCache.TryRemove("oidc");
    }

    private void SetSecretIfProvided(ExternalLoginProviderSettings provider, string? clientSecret)
    {
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            provider.ProtectedClientSecret = _protector.Protect(clientSecret.Trim());
        }
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
