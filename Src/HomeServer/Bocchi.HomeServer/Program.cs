using System.Globalization;

using Bocchi.Generator;
using Bocchi.HomeServer;
using Bocchi.HomeServer.Build;
using Bocchi.HomeServer.Components;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Security;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;
using Bocchi.Workspace.DependencyInjection;
using Bocchi.Workspace.State;

using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

    // Workspace 必须先注册：日志的文件 sink 也要落到 <workspace>/.bocchi/logs。
    builder.Services.AddBocchiWorkspace(
        builder.Configuration,
        sp => builder.Environment.ContentRootPath);
    builder.Services.AddBocchiGenerator(builder.Configuration);
    builder.Services.AddSingleton<BuildOrchestrator>();
    builder.Services.AddDbContext<BocchiDbContext>((sp, options) =>
    {
        var layout = sp.GetRequiredService<WorkspaceLayout>();
        Directory.CreateDirectory(layout.BocchiDirectory);
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
            options.User.RequireUniqueEmail = true;
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
    builder.Services.AddSingleton<ExternalLoginOptionsConfigurator>();
    builder.Services.AddSingleton<IConfigureOptions<OAuthOptions>>(sp => sp.GetRequiredService<ExternalLoginOptionsConfigurator>());
    builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>>(sp => sp.GetRequiredService<ExternalLoginOptionsConfigurator>());
    builder.Services.AddScoped<HomeServerSetupService>();
    builder.Services.AddScoped<DashboardSettingsService>();
    builder.Services.AddScoped<ExternalLoginSettingsService>();
    builder.Services.AddScoped<ThemeSettingsService>();
    builder.Services.AddScoped<ContentEditingService>();
    builder.Services.AddScoped<PreviewRouteMapService>();
    builder.Services.AddScoped<PreviewHost>();

    var workspaceRootForKeys = ResolveWorkspaceRoot(builder.Configuration, builder.Environment.ContentRootPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(workspaceRootForKeys, ".bocchi", "data-protection-keys")));

    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var layout = services.GetRequiredService<WorkspaceLayout>();
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
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkspaceOptions>>().Value;
        if (options.AutoInitialize)
        {
            await sp.GetRequiredService<WorkspaceInitializer>().InitializeAsync();
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

    app.UseAntiforgery();
    app.UseBocchiSetupGate();
    app.UseAuthentication();
    app.UseAuthorization();

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

    app.MapBuildEndpoints();

    app.MapGet("/", (PreviewHost preview, CancellationToken cancellationToken)
            => preview.RenderAsync(null, cancellationToken))
        .RequireAuthorization();

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode()
        .RequireAuthorization("Admin");

    app.MapGet("/{**previewPath}", (string? previewPath, PreviewHost preview, CancellationToken cancellationToken)
            => preview.RenderAsync(previewPath, cancellationToken))
        .RequireAuthorization();

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

/// <summary>根据配置解析 Data Protection key 所在工作区根目录。</summary>
static string ResolveWorkspaceRoot(IConfiguration configuration, string contentRootPath)
{
    var configured = configuration[$"{WorkspaceOptions.SectionName}:WorkspaceRoot"];
    if (string.IsNullOrWhiteSpace(configured))
    {
        return Path.Combine(contentRootPath, "workspace");
    }

    return Path.IsPathRooted(configured)
        ? Path.GetFullPath(configured)
        : Path.GetFullPath(Path.Combine(contentRootPath, configured));
}

/// <summary>
/// 入口类型。声明为 <c>partial</c> 是为了让集成测试中的
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> 能够引用顶层程序。
/// </summary>
public partial class Program;
