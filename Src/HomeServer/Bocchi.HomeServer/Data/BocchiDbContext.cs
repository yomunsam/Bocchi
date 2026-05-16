using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Data;

/// <summary>
/// Home Server 应用数据库。内容正文仍在 Markdown/YAML 文件中；这里保存 Identity、Setup、设置和管理投影。
/// </summary>
public sealed class BocchiDbContext : IdentityDbContext<BocchiUser, IdentityRole, string>
{
    /// <summary>构造 EF Core DbContext。</summary>
    public BocchiDbContext(DbContextOptions<BocchiDbContext> options)
        : base(options)
    {
    }

    /// <summary>单站点 Setup 状态。</summary>
    public DbSet<SetupState> SetupStates => Set<SetupState>();

    /// <summary>Dashboard 自身设置。</summary>
    public DbSet<DashboardSettings> DashboardSettings => Set<DashboardSettings>();

    /// <summary>第三方登录 Provider 设置。</summary>
    public DbSet<ExternalLoginProviderSettings> ExternalLoginProviders => Set<ExternalLoginProviderSettings>();

    /// <summary>前台业务 Theme 配置投影。</summary>
    public DbSet<ThemeConfigurationRecord> ThemeConfigurations => Set<ThemeConfigurationRecord>();

    /// <summary>站点本地化设置。</summary>
    public DbSet<LocalizationSettingsRecord> LocalizationSettings => Set<LocalizationSettingsRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);

        builder.Entity<BocchiUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.LastLoginAt);
            entity.Property(x => x.IsDisabled).IsRequired();
        });

        builder.Entity<SetupState>(entity =>
        {
            entity.ToTable("HomeServerSetupStates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FirstAdminUserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.WorkspaceRoot).HasMaxLength(2048).IsRequired();
        });

        builder.Entity<DashboardSettings>(entity =>
        {
            entity.ToTable("DashboardSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SiteTitle).HasMaxLength(160).IsRequired();
            entity.Property(x => x.SiteDescription).HasMaxLength(512).IsRequired();
            entity.Property(x => x.AppearanceMode).HasMaxLength(16).IsRequired();
        });

        builder.Entity<ExternalLoginProviderSettings>(entity =>
        {
            entity.ToTable("ExternalLoginProviderSettings");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ProviderKey).IsUnique();
            entity.Property(x => x.ProviderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ClientId).HasMaxLength(512);
            entity.Property(x => x.ProtectedClientSecret).HasMaxLength(4096);
            entity.Property(x => x.CallbackPath).HasMaxLength(256);
            entity.Property(x => x.Authority).HasMaxLength(1024);
            entity.Property(x => x.ResponseType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Scopes).HasMaxLength(512).IsRequired();
            entity.Property(x => x.NameClaimType).HasMaxLength(256);
            entity.Property(x => x.EmailClaimType).HasMaxLength(256);
        });

        builder.Entity<ThemeConfigurationRecord>(entity =>
        {
            entity.ToTable("ThemeConfigurationRecords");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ThemeId).IsUnique();
            entity.Property(x => x.ThemeId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ConfigurationJson).IsRequired();
        });

        builder.Entity<LocalizationSettingsRecord>(entity =>
        {
            entity.ToTable("LocalizationSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PrimaryLanguage).HasMaxLength(32).IsRequired();
            entity.Property(x => x.EnabledLanguagesJson).IsRequired();
            entity.Property(x => x.CustomLanguagesJson).IsRequired();
            entity.Property(x => x.UrlPolicy).HasMaxLength(64).IsRequired();
        });
    }
}
