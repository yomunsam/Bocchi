using Bocchi.HomeServer.Data;

using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// GitHub 集成设置服务。它是 GitHub OAuth App 配置的唯一写入点，
/// GitHub 登录和发布 Device Flow 共享这里的 client id。
/// </summary>
public sealed class GitHubIntegrationSettingsService
{
    private readonly BocchiDbContext _db;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly IOptionsMonitorCache<OAuthOptions> _oauthCache;

    /// <summary>构造 GitHub 集成设置服务。</summary>
    public GitHubIntegrationSettingsService(
        BocchiDbContext db,
        IDataProtectionProvider protection,
        TimeProvider time,
        IOptionsMonitorCache<OAuthOptions> oauthCache)
    {
        _db = db;
        // 复用旧 GitHub 登录 secret 的 protector purpose，迁移到 GitHubIntegrationSettings 后无需用户重新粘贴 secret。
        _protector = protection.CreateProtector("Bocchi.HomeServer.ExternalLoginProviderSettings.v1");
        _time = time;
        _oauthCache = oauthCache;
    }

    /// <summary>读取单站点 GitHub 集成设置；缺失时创建默认值。</summary>
    public async Task<GitHubIntegrationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.GitHubIntegrationSettings
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (settings is not null)
        {
            return settings;
        }

        settings = new GitHubIntegrationSettings { Id = 1, UpdatedAt = _time.GetUtcNow() };
        _db.GitHubIntegrationSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return settings;
    }

    /// <summary>保存 GitHub OAuth App 设置；secret 留空时保留现有值。</summary>
    public async Task SaveAsync(GitHubIntegrationSettingsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        settings.DisplayName = string.IsNullOrWhiteSpace(update.DisplayName) ? "GitHub" : update.DisplayName.Trim();
        settings.LoginEnabled = update.LoginEnabled;
        settings.OAuthClientId = TrimToNull(update.OAuthClientId);
        settings.CallbackPath = string.IsNullOrWhiteSpace(update.CallbackPath) ? "/signin-github" : update.CallbackPath.Trim();
        if (!string.IsNullOrWhiteSpace(update.OAuthClientSecret))
        {
            settings.ProtectedOAuthClientSecret = _protector.Protect(update.OAuthClientSecret.Trim());
        }

        settings.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _oauthCache.TryRemove("github");
    }

    /// <summary>读取受保护 secret 明文；只在配置 OAuth options 时短暂使用。</summary>
    public string UnprotectSecret(GitHubIntegrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return string.IsNullOrWhiteSpace(settings.ProtectedOAuthClientSecret)
            ? string.Empty
            : _protector.Unprotect(settings.ProtectedOAuthClientSecret);
    }

    /// <summary>判断 GitHub 登录按钮是否可展示。</summary>
    public static bool IsReadyForLogin(GitHubIntegrationSettings settings)
        => settings.LoginEnabled
           && !string.IsNullOrWhiteSpace(settings.OAuthClientId)
           && !string.IsNullOrWhiteSpace(settings.ProtectedOAuthClientSecret);

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>保存 GitHub 集成设置的输入模型；OAuthClientSecret 为空时保留已有 secret。</summary>
public sealed record GitHubIntegrationSettingsUpdate(
    bool LoginEnabled,
    string DisplayName,
    string? OAuthClientId,
    string? OAuthClientSecret,
    string? CallbackPath);
