using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Home Server 首次启动与数据库初始化服务。它是 M4 访问边界的单一判断入口。
/// </summary>
public sealed class HomeServerSetupService
{
    private readonly BocchiDbContext _db;
    private readonly UserManager<BocchiUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly BocchiDataLayout _layout;
    private readonly TimeProvider _time;
    private readonly SiteProfileSettingsService _siteProfile;
    private readonly DashboardGuideService _guides;

    /// <summary>构造 Setup 服务。</summary>
    public HomeServerSetupService(
        BocchiDbContext db,
        UserManager<BocchiUser> users,
        RoleManager<IdentityRole> roles,
        BocchiDataLayout layout,
        TimeProvider time,
        SiteProfileSettingsService siteProfile,
        DashboardGuideService guides)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _layout = layout;
        _time = time;
        _siteProfile = siteProfile;
        _guides = guides;
    }

    /// <summary>应用 EF Core 迁移并确保基础种子数据存在。</summary>
    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_layout.StateDirectory);
        await _db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await EnsureAdminRoleAsync().ConfigureAwait(false);
        await EnsureDashboardSettingsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureGitHubIntegrationSettingsAsync(cancellationToken).ConfigureAwait(false);
        await _siteProfile.EnsureAsync(cancellationToken).ConfigureAwait(false);
        await EnsureLocalizationSettingsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureExternalProviderDefaultsAsync(cancellationToken).ConfigureAwait(false);
        await _guides.EnsureBuiltInAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>判断 Setup 是否已经完成，并且至少存在一个 Admin 用户。</summary>
    public async Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var hasSetup = await _db.SetupStates.AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false);
            if (!hasSetup)
            {
                return false;
            }

            var adminUsers = await _users.GetUsersInRoleAsync(BocchiRoleNames.Admin).ConfigureAwait(false);
            return adminUsers.Count > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            return false;
        }
    }

    /// <summary>创建第一个 Admin 用户并写入 SetupState。</summary>
    public async Task<IdentityResult> CreateFirstAdminAsync(
        string userName,
        string password,
        string? displayName,
        CancellationToken cancellationToken = default)
        => await CreateFirstAdminAsync(userName, password, displayName, null, null, cancellationToken).ConfigureAwait(false);

    /// <summary>创建第一个 Admin 用户、写入站点基础约定并完成 SetupState。</summary>
    public async Task<IdentityResult> CreateFirstAdminAsync(
        string userName,
        string password,
        string? displayName,
        string? email,
        SiteProfileSettingsUpdate? siteProfile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
        if (await IsSetupCompleteAsync(cancellationToken).ConfigureAwait(false))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "SetupAlreadyCompleted",
                Description = "Setup has already been completed.",
            });
        }

        if (siteProfile is not null)
        {
            await _siteProfile.SaveAsync(siteProfile, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _siteProfile.EnsureAsync(cancellationToken).ConfigureAwait(false);
        }

        var now = _time.GetUtcNow();
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        var normalizedUserName = userName.Trim();
        var user = new BocchiUser
        {
            UserName = normalizedUserName,
            Email = normalizedEmail,
            EmailConfirmed = !string.IsNullOrWhiteSpace(normalizedEmail),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUserName : displayName.Trim(),
            CreatedAt = now,
            LastLoginAt = now,
        };

        var create = await _users.CreateAsync(user, password).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            return create;
        }

        var role = await _users.AddToRoleAsync(user, BocchiRoleNames.Admin).ConfigureAwait(false);
        if (!role.Succeeded)
        {
            return role;
        }

        _db.SetupStates.Add(new SetupState
        {
            Id = 1,
            CompletedAt = now,
            FirstAdminUserId = user.Id,
            DataRoot = _layout.DataRoot,
            SchemaVersion = 1,
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return IdentityResult.Success;
    }

    private async Task EnsureAdminRoleAsync()
    {
        if (!await _roles.RoleExistsAsync(BocchiRoleNames.Admin).ConfigureAwait(false))
        {
            var result = await _roles.CreateAsync(new IdentityRole(BocchiRoleNames.Admin)).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var details = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"无法创建 Admin role：{details}");
            }
        }
    }

    private async Task EnsureDashboardSettingsAsync(CancellationToken cancellationToken)
    {
        if (!await _db.DashboardSettings.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            _db.DashboardSettings.Add(new DashboardSettings { Id = 1, UpdatedAt = _time.GetUtcNow() });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureGitHubIntegrationSettingsAsync(CancellationToken cancellationToken)
    {
        if (!await _db.GitHubIntegrationSettings.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            _db.GitHubIntegrationSettings.Add(new GitHubIntegrationSettings { Id = 1, UpdatedAt = _time.GetUtcNow() });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task EnsureExternalProviderDefaultsAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.ExternalLoginProviders
            .Select(x => x.ProviderKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!existing.Contains("oidc", StringComparer.OrdinalIgnoreCase))
        {
            _db.ExternalLoginProviders.Add(new ExternalLoginProviderSettings
            {
                ProviderKey = "oidc",
                DisplayName = "OpenID Connect",
                CallbackPath = "/signin-oidc-custom",
                Authority = string.Empty,
                UpdatedAt = _time.GetUtcNow(),
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>确保站点本地化设置存在，供 Settings / Localization 首次打开时直接编辑。</summary>
    private async Task EnsureLocalizationSettingsAsync(CancellationToken cancellationToken)
    {
        if (!await _db.LocalizationSettings.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            var primaryLanguage = LocalizationSettingsService.BuiltInLanguages
                .First(x => string.Equals(x.Code, LocalizationSettingsService.DefaultPrimaryLanguage, StringComparison.Ordinal));
            _db.LocalizationSettings.Add(new LocalizationSettingsRecord
            {
                Id = 1,
                PrimaryLanguage = primaryLanguage.Code,
                EnabledLanguagesJson = System.Text.Json.JsonSerializer.Serialize(new[] { primaryLanguage }),
                CustomLanguagesJson = "[]",
                CommonTextOverridesJson = "{}",
                UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
                UpdatedAt = _time.GetUtcNow(),
            });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
