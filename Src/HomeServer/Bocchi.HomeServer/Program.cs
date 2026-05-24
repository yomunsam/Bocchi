using System.Globalization;

using Bocchi.Generator;
using Bocchi.Generator.Theme;
using Bocchi.HomeServer;
using Bocchi.HomeServer.Build;
using Bocchi.HomeServer.Components;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Security;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Ai;
using Bocchi.HomeServer.Services.Git;
using Bocchi.HomeServer.Services.Publishing;
using Bocchi.Workspace;
using Bocchi.Workspace.DependencyInjection;
using Bocchi.Workspace.State;

using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Serilog;

// 文件日志保留 SourceContext 与结构化属性，便于排查 console 里被筛掉的框架细节。
const string FileLogOutputTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}";

// Bootstrap logger so any failure during configuration is captured.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Bocchi Home Server");

    var builder = WebApplication.CreateBuilder(args);
    ApplyDevelopmentDataRootDefault(builder);

    // DataRoot 必须先注册：数据库、日志和内容 workspace 路径都从这里统一解析。
    var dataRootBasePath = ResolveDataRootBase(builder.Environment);
    builder.Services.AddBocchiData(
        builder.Configuration,
        _ => dataRootBasePath);
    builder.Services.AddBocchiGenerator(builder.Configuration);
    builder.Services.PostConfigure<ThemeDevelopmentOptions>(options => options.EnvironmentName = builder.Environment.EnvironmentName);
    builder.Services.AddSingleton<BuildOrchestrator>();
    builder.Services.AddDbContext<BocchiDbContext>((sp, options) =>
    {
        var layout = sp.GetRequiredService<BocchiDataLayout>();
        Directory.CreateDirectory(layout.StateDirectory);
        options.UseSqlite($"Data Source={layout.SqliteDatabasePath}");
    });
    builder.Services.AddIdentity<BocchiUser, IdentityRole>(options =>
        {
            // Home Server 是本机私有工具，密码策略要安全但不过分劝退普通创作者。
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.User.RequireUniqueEmail = false;
        })
        .AddEntityFrameworkStores<BocchiDbContext>()
        .AddDefaultTokenProviders();
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
    });
    builder.Services.AddAuthentication()
        .AddOAuth("github", _ => { })
        .AddOpenIdConnect("oidc", _ => { });
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Admin", policy => policy.RequireRole(BocchiRoleNames.Admin));
    });
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<ExternalLoginOptionsConfigurator>();
    builder.Services.AddSingleton<IConfigureOptions<OAuthOptions>>(sp => sp.GetRequiredService<ExternalLoginOptionsConfigurator>());
    builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>>(sp => sp.GetRequiredService<ExternalLoginOptionsConfigurator>());
    builder.Services.AddScoped<HomeServerSetupService>();
    builder.Services.AddScoped<DashboardGuideService>();
    builder.Services.AddScoped<DashboardSettingsService>();
    builder.Services.AddScoped<GitHubIntegrationSettingsService>();
    builder.Services.AddScoped<SiteProfileSettingsService>();
    builder.Services.AddScoped<ExternalLoginSettingsService>();
    builder.Services.AddScoped<ThemeSettingsService>();
    builder.Services.AddScoped<LocalizationSettingsService>();
    builder.Services.AddScoped<CategoryTreeService>();
    builder.Services.Configure<GitHubDeviceFlowOptions>(builder.Configuration.GetSection(GitHubDeviceFlowOptions.SectionName));
    builder.Services.AddScoped<GitProviderConnectionService>();
    builder.Services.AddScoped<ContentWorkspaceRemoteService>();
    builder.Services.AddScoped<PublishPlanService>();
    builder.Services.AddScoped<PublishExecutionService>();
    builder.Services.AddScoped<IStaticSiteBuildRunner, StaticSiteBuildRunner>();
    builder.Services.AddHttpClient<GitHubPagesPublisher>();
    builder.Services.AddHttpClient<GitHubDeviceFlowService>();
    builder.Services.AddScoped<IPublishTargetPublisher>(sp => sp.GetRequiredService<GitHubPagesPublisher>());
    builder.Services.AddScoped<NavigationMenuService>();
    builder.Services.AddScoped<ThemeMigrationService>();
    builder.Services.AddScoped<DashboardAiClient>();
    builder.Services.AddSingleton<DashboardLocalizationService>();
    builder.Services.AddScoped<ContentEditingService>();
    builder.Services.AddScoped<ContentLanguageVersionService>();
    builder.Services.AddScoped<EditorDraftService>();
    builder.Services.AddScoped<NoteCreationService>();
    builder.Services.AddScoped<PreviewRouteMapService>();
    builder.Services.AddScoped<PreviewHost>();
    builder.Services.Configure<RequestLocalizationOptions>(options =>
    {
        // Dashboard UI language 是后台偏好；当前只让 RequestLocalization 影响 Admin 文案，不进入 Theme Contract。
        var cultures = DashboardLocalizationService.SupportedDashboardLanguages
            .Select(x => CultureInfo.GetCultureInfo(x.Code))
            .ToArray();
        options.DefaultRequestCulture = new RequestCulture(DashboardLocalizationService.DefaultLanguageCode);
        options.SupportedCultures = cultures;
        options.SupportedUICultures = cultures;
    });

    var dataRootForKeys = ResolveDataRoot(builder.Configuration, dataRootBasePath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataRootForKeys, "state", "data-protection-keys")));

    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var layout = services.GetRequiredService<BocchiDataLayout>();
            Directory.CreateDirectory(layout.LogsDirectory);
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    Path.Combine(layout.LogsDirectory, "home-server-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: FileLogOutputTemplate,
                    formatProvider: CultureInfo.InvariantCulture);
        });
    }

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // 启动时执行：可选自动初始化 + 自动迁移 schema。
    using (var scope = app.Services.CreateScope())
    {
        var sp = scope.ServiceProvider;
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BocchiDataOptions>>().Value;
        if (options.AutoInitialize)
        {
            await sp.GetRequiredService<BocchiDataInitializer>().InitializeAsync();
        }

        if (options.AutoMigrateSchema)
        {
            await sp.GetRequiredService<SchemaMigrator>().MigrateAsync();
        }

        await sp.GetRequiredService<HomeServerSetupService>().EnsureDatabaseAsync();
    }

    if (!app.Environment.IsEnvironment("Testing"))
    {
        app.UseSerilogRequestLogging();
    }

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseRequestLocalization();
    app.UseAntiforgery();
    app.UseBocchiSetupGate();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseBocchiAdminGate();

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    app.MapStaticAssets();

    app.MapHealthChecks("/healthz", new HealthCheckOptions
    {
        ResponseWriter = static async (httpContext, report) =>
        {
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            await httpContext.Response.WriteAsync(report.Status.ToString());
        },
    });

    app.MapBocchiAccountEndpoints();
    app.MapDashboardLocalizationEndpoints();
    app.MapDashboardHomeEndpoints();

    app.MapBuildEndpoints();

    app.MapGet("/", (PreviewHost preview, CancellationToken cancellationToken)
            => preview.RenderAsync(null, cancellationToken))
        .RequireAuthorization();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.MapGet("/{**previewPath}", RenderPreviewOrStaticAssetMissAsync);

    // CLI 子命令：`Bocchi.HomeServer -- build [--theme=...] [--include-drafts]` 跑完即退出。
    if (BuildCli.TryParse(args, out var cliOptions))
    {
        await using (app)
        {
            using var scope = app.Services.CreateScope();
            var exitCode = await BuildCli.RunAsync(scope.ServiceProvider, cliOptions, default);
            Environment.ExitCode = exitCode;
        }

        return;
    }

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Bocchi Home Server terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>开发期默认 DataRoot 避开源码目录，避免 macOS/Windows 大小写不敏感文件系统把 <c>data/</c> 合并进 <c>Data/</c>。</summary>
static void ApplyDevelopmentDataRootDefault(WebApplicationBuilder builder)
{
    var key = $"{BocchiDataOptions.SectionName}:DataRoot";
    if (!builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(builder.Configuration[key]))
    {
        return;
    }

    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [key] = ".bocchi-dev-data",
    });
}

/// <summary>解析相对 DataRoot 的基准路径：开发期贴近项目目录，发布后贴近程序目录。</summary>
static string ResolveDataRootBase(IHostEnvironment environment)
    => environment.IsDevelopment() ? environment.ContentRootPath : AppContext.BaseDirectory;

/// <summary>根据配置解析 DataRoot 绝对路径，供 Data Protection 在 DI 完成前落盘。</summary>
static string ResolveDataRoot(IConfiguration configuration, string dataRootBasePath)
{
    var configured = configuration[$"{BocchiDataOptions.SectionName}:DataRoot"];
    if (string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(Path.Combine(dataRootBasePath, "data"));
    }

    return Path.IsPathRooted(configured)
        ? Path.GetFullPath(configured)
        : Path.GetFullPath(Path.Combine(dataRootBasePath, configured));
}

/// <summary>渲染前台 preview；后台静态资源缺失时明确 404，避免 catch-all 返回 HTML 污染 JS/manifest/JSON 请求。</summary>
static Task<IResult> RenderPreviewOrStaticAssetMissAsync(
    HttpContext context,
    string? previewPath,
    PreviewHost preview,
    CancellationToken cancellationToken)
{
    if (HomeServerStaticAssetPaths.IsHomeServerAsset(context.Request.Path))
    {
        context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
        return Task.FromResult<IResult>(Results.NotFound());
    }

    if (context.User.Identity?.IsAuthenticated != true)
    {
        return Task.FromResult<IResult>(Results.Challenge());
    }

    return preview.RenderAsync(previewPath, cancellationToken);
}

/// <summary>
/// 入口类型。声明为 <c>partial</c> 是为了让集成测试中的
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> 能够引用顶层程序。
/// </summary>
public partial class Program;
