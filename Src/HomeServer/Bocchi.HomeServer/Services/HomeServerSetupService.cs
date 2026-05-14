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
    private readonly WorkspaceLayout _layout;
    private readonly TimeProvider _time;

    /// <summary>构造 Setup 服务。</summary>
    public HomeServerSetupService(
        BocchiDbContext db,
        UserManager<BocchiUser> users,
        RoleManager<IdentityRole> roles,
        WorkspaceLayout layout,
        TimeProvider time)
    {
        _db = db;
        _users = users;
        _roles = roles;
        _layout = layout;
        _time = time;
    }

    /// <summary>应用 EF Core 迁移并确保基础种子数据存在。</summary>
    public async Task EnsureDatabaseAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_layout.BocchiDirectory);
        await _db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await EnsureAdminRoleAsync().ConfigureAwait(false);
        await EnsureDashboardSettingsAsync(cancellationToken).ConfigureAwait(false);
        await EnsureExternalProviderDefaultsAsync(cancellationToken).ConfigureAwait(false);
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
        string email,
        string password,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
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

        var now = _time.GetUtcNow();
        var user = new BocchiUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName.Trim(),
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
            WorkspaceRoot = _layout.Root,
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

    private async Task EnsureExternalProviderDefaultsAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.ExternalLoginProviders
            .Select(x => x.ProviderKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!existing.Contains("github", StringComparer.OrdinalIgnoreCase))
        {
            _db.ExternalLoginProviders.Add(new ExternalLoginProviderSettings
            {
                ProviderKey = "github",
                DisplayName = "GitHub",
                CallbackPath = "/signin-github",
                UpdatedAt = _time.GetUtcNow(),
            });
        }

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
}
