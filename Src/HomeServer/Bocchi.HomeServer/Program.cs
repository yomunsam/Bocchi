using System.Globalization;
using Bocchi.HomeServer.Components;
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

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddHealthChecks();

    var app = builder.Build();

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
