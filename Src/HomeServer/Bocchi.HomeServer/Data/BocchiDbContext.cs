using Bocchi.HomeServer.Data.State;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Data;

/// <summary>
/// Home Server 应用数据库。内容正文仍在 Markdown/YAML 文件中；这里保存 Identity、Setup、设置、
/// 管理投影，以及内容扫描索引和构建记录。整个 SQLite 文件只由这一套 EF Core migration 管理。
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

    /// <summary>GitHub OAuth App 集成设置。</summary>
    public DbSet<GitHubIntegrationSettings> GitHubIntegrationSettings => Set<GitHubIntegrationSettings>();

    /// <summary>Home Server 权威的站点基础约定。</summary>
    public DbSet<SiteProfileSettings> SiteProfileSettings => Set<SiteProfileSettings>();

    /// <summary>第三方登录 Provider 设置。</summary>
    public DbSet<ExternalLoginProviderSettings> ExternalLoginProviders => Set<ExternalLoginProviderSettings>();

    /// <summary>前台业务 Theme 配置投影。</summary>
    public DbSet<ThemeConfigurationRecord> ThemeConfigurations => Set<ThemeConfigurationRecord>();

    /// <summary>站点本地化设置。</summary>
    public DbSet<LocalizationSettingsRecord> LocalizationSettings => Set<LocalizationSettingsRecord>();

    /// <summary>Dashboard 首页 Guide 堆栈状态。</summary>
    public DbSet<DashboardGuideCardRecord> DashboardGuideCards => Set<DashboardGuideCardRecord>();

    /// <summary>后台维护的分类树；当前不参与前台 Menu。</summary>
    public DbSet<CategoryTreeRecord> CategoryTrees => Set<CategoryTreeRecord>();

    /// <summary>发布方案配置；凭据字段只保存受保护后的字符串。</summary>
    public DbSet<PublishPlanRecord> PublishPlans => Set<PublishPlanRecord>();

    /// <summary>发布运行历史；只保存脱敏后的执行摘要。</summary>
    public DbSet<PublishRunRecord> PublishRuns => Set<PublishRunRecord>();

    /// <summary>Git provider 授权连接；凭据字段只保存受保护后的字符串。</summary>
    public DbSet<GitProviderConnectionRecord> GitProviderConnections => Set<GitProviderConnectionRecord>();

    /// <summary>内容 workspace Git remote 配置。</summary>
    public DbSet<ContentWorkspaceRemoteRecord> ContentWorkspaceRemotes => Set<ContentWorkspaceRemoteRecord>();

    /// <summary>内容 workspace 源文件索引。</summary>
    public DbSet<ContentFileRecord> ContentFiles => Set<ContentFileRecord>();

    /// <summary>内容摘要索引。</summary>
    public DbSet<ContentItemRecord> ContentItems => Set<ContentItemRecord>();

    /// <summary>内容扫描运行。</summary>
    public DbSet<ContentScanRunRecord> ContentScanRuns => Set<ContentScanRunRecord>();

    /// <summary>内容扫描诊断。</summary>
    public DbSet<ContentErrorRecord> ContentErrors => Set<ContentErrorRecord>();

    /// <summary>站点构建运行。</summary>
    public DbSet<BuildRunRecord> BuildRuns => Set<BuildRunRecord>();

    /// <summary>构建产物登记。</summary>
    public DbSet<BuildArtifactRecord> BuildArtifacts => Set<BuildArtifactRecord>();

    /// <summary>构建阶段日志。</summary>
    public DbSet<BuildStageLogRecord> BuildStageLogs => Set<BuildStageLogRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // SQLite 没有原生 DateTimeOffset：默认存成 TEXT 后 EF 无法在服务端排序/比较。
        // 统一存成 UTC ticks（INTEGER），不同 offset 的时间也按真实先后排序；读出来是 +00:00。
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

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
            entity.Property(x => x.DataRoot).HasMaxLength(2048).IsRequired();
        });

        builder.Entity<DashboardSettings>(entity =>
        {
            entity.ToTable("DashboardSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AppearanceMode).HasMaxLength(16).IsRequired();
        });

        builder.Entity<GitHubIntegrationSettings>(entity =>
        {
            entity.ToTable("GitHubIntegrationSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.LoginEnabled).IsRequired();
            entity.Property(x => x.OAuthClientId).HasMaxLength(512);
            entity.Property(x => x.ProtectedOAuthClientSecret).HasMaxLength(4096);
            entity.Property(x => x.CallbackPath).HasMaxLength(256).IsRequired();
        });

        builder.Entity<SiteProfileSettings>(entity =>
        {
            entity.ToTable("SiteProfileSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SiteName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.DefaultTitle).HasMaxLength(220).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(512).IsRequired();
            entity.Property(x => x.PublicBaseUrl).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.CopyrightNotice).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Language).HasMaxLength(32).IsRequired();
            entity.Property(x => x.TimeZone).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DefaultThemeId).HasMaxLength(160).IsRequired();
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
            entity.Property(x => x.I18nTextOverridesJson).IsRequired();
        });

        builder.Entity<LocalizationSettingsRecord>(entity =>
        {
            entity.ToTable("LocalizationSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PrimaryLanguage).HasMaxLength(32).IsRequired();
            entity.Property(x => x.EnabledLanguagesJson).IsRequired();
            entity.Property(x => x.CustomLanguagesJson).IsRequired();
            entity.Property(x => x.CommonTextOverridesJson).IsRequired();
            entity.Property(x => x.UrlPolicy).HasMaxLength(64).IsRequired();
        });

        builder.Entity<DashboardGuideCardRecord>(entity =>
        {
            entity.ToTable("DashboardGuideCards");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SortOrder).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
        });

        builder.Entity<CategoryTreeRecord>(entity =>
        {
            entity.ToTable("CategoryTrees");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Scope).IsUnique();
            entity.Property(x => x.Scope).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TreeJson).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
        });

        builder.Entity<PublishPlanRecord>(entity =>
        {
            entity.ToTable("PublishPlans");
            entity.HasKey(x => x.Id);
            entity.HasOne<GitProviderConnectionRecord>()
                .WithMany()
                .HasForeignKey(x => x.GitProviderConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Channel).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ConfigurationJson).IsRequired();
            entity.Property(x => x.ProtectedCredentialJson).HasMaxLength(8192);
            entity.Property(x => x.IsDefault).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
        });

        builder.Entity<PublishRunRecord>(entity =>
        {
            entity.ToTable("PublishRuns");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.StartedAt);
            entity.HasOne<PublishPlanRecord>()
                .WithMany()
                .HasForeignKey(x => x.PublishPlanId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Channel).HasMaxLength(64).IsRequired();
            entity.Property(x => x.StartedAt).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasOne<BuildRunRecord>()
                .WithMany()
                .HasForeignKey(x => x.BuildRunId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.BuildSessionId).HasMaxLength(64);
            entity.Property(x => x.BuildFingerprint).HasMaxLength(128);
            entity.Property(x => x.RemoteCommitSha).HasMaxLength(128);
            entity.Property(x => x.RemoteUrl).HasMaxLength(2048);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2048);
        });

        builder.Entity<GitProviderConnectionRecord>(entity =>
        {
            entity.ToTable("GitProviderConnections");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProviderKey, x.BaseUrl, x.AccountLogin });
            entity.Property(x => x.ProviderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.AccountLogin).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Scopes).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.ProtectedCredentialJson).HasMaxLength(8192).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
        });

        builder.Entity<ContentWorkspaceRemoteRecord>(entity =>
        {
            entity.ToTable("ContentWorkspaceRemotes");
            entity.HasKey(x => x.Id);
            entity.HasOne<GitProviderConnectionRecord>()
                .WithMany()
                .HasForeignKey(x => x.GitProviderConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(x => x.RemoteName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RemoteUrl).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.Branch).HasMaxLength(256).IsRequired();
            entity.Property(x => x.LastSyncStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.LastSyncMessage).HasMaxLength(2048);
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
        });

        ConfigureContentState(builder);
        ConfigureBuildState(builder);
    }

    /// <summary>内容扫描索引：文件 → 内容条目级联；扫描运行 → 诊断级联。</summary>
    private static void ConfigureContentState(ModelBuilder builder)
    {
        builder.Entity<ContentFileRecord>(entity =>
        {
            entity.ToTable("ContentFiles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RelativePath).IsUnique();
            entity.HasIndex(x => x.Kind);
            entity.Property(x => x.RelativePath).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.HasMany(x => x.Items)
                .WithOne(x => x.File)
                .HasForeignKey(x => x.FileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ContentItemRecord>(entity =>
        {
            entity.ToTable("ContentItems");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.Kind, x.ContentId }).IsUnique();
            entity.HasIndex(x => new { x.Kind, x.PublishedAt });
            entity.HasIndex(x => new { x.Kind, x.LocalizationGroup, x.Language });
            entity.Property(x => x.ContentId).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(256);
            entity.Property(x => x.Year).HasMaxLength(8);
            entity.Property(x => x.Language).HasMaxLength(32);
            entity.Property(x => x.LocalizationGroup).HasMaxLength(256);
            entity.Property(x => x.SourceLanguage).HasMaxLength(32);
            entity.Property(x => x.SourceContentId).HasMaxLength(512);
        });

        builder.Entity<ContentScanRunRecord>(entity =>
        {
            entity.ToTable("ContentScanRuns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.GitHeadSha).HasMaxLength(64);
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.HasMany(x => x.Errors)
                .WithOne(x => x.ScanRun)
                .HasForeignKey(x => x.ScanRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ContentErrorRecord>(entity =>
        {
            entity.ToTable("ContentErrors");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Severity);
            entity.Property(x => x.RelativePath).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.Field).HasMaxLength(256);
            entity.Property(x => x.Code).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Message).IsRequired();
        });
    }

    /// <summary>构建记录：构建运行 → 产物/日志级联；扫描运行被删除时构建运行的引用置空。</summary>
    private static void ConfigureBuildState(ModelBuilder builder)
    {
        builder.Entity<BuildRunRecord>(entity =>
        {
            entity.ToTable("BuildRuns");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SessionId).IsUnique();
            entity.HasIndex(x => x.StartedAt);
            entity.Property(x => x.Mode).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Environment).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ThemeId).HasMaxLength(160);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Fingerprint).HasMaxLength(128);
            entity.Property(x => x.BocchiVersion).HasMaxLength(64);
            entity.HasOne(x => x.ScanRun)
                .WithMany()
                .HasForeignKey(x => x.ScanRunId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(x => x.Artifacts)
                .WithOne(x => x.BuildRun)
                .HasForeignKey(x => x.BuildRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Logs)
                .WithOne(x => x.BuildRun)
                .HasForeignKey(x => x.BuildRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BuildArtifactRecord>(entity =>
        {
            entity.ToTable("BuildArtifacts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Path);
            entity.Property(x => x.Path).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ProducedBy).HasMaxLength(128).IsRequired();
        });

        builder.Entity<BuildStageLogRecord>(entity =>
        {
            entity.ToTable("BuildStageLogs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Level);
            entity.Property(x => x.Stage).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Level).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Message).IsRequired();
        });
    }
}
