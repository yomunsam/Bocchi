using System.Globalization;
using Bocchi.HomeServer.Components;
using Bocchi.Workspace;
using Bocchi.Workspace.DependencyInjection;
using Bocchi.Workspace.State;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

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
                formatProvider: CultureInfo.InvariantCulture);
    });

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
    }

    app.UseSerilogRequestLogging();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

    app.UseAntiforgery();

    app.MapStaticAssets();

    app.MapHealthChecks("/healthz", new HealthCheckOptions
    {
        ResponseWriter = static async (httpContext, report) =>
        {
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            await httpContext.Response.WriteAsync(report.Status.ToString());
        },
    });

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

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

/// <summary>
/// 入口类型。声明为 <c>partial</c> 是为了让集成测试中的
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> 能够引用顶层程序。
/// </summary>
public partial class Program;
