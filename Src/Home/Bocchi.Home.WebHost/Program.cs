using Bocchi.Home.WebHost.Components;
using Serilog;

#region Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
#endregion

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configuration (json)

}
catch (Exception ex)
{
    Log.Fatal(ex, "Bocchi Application terminated unexpectedly");
    return;
}
finally
{
    Log.Information("Bocchi Application is shutting down");
    Log.CloseAndFlush();
}


// Razor Pages
builder.Services.AddRazorPages();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();



var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();

// Razor Pages
app.MapRazorPages();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();


//------------

static void BuildConfiguration(WebApplicationBuilder builder, string[]? jsonFileNames)
{
    bool isContainer = builder.Environment.IsContainer();
    // 干掉默认的配置来源
    builder.Configuration.Sources.Clear();

    #region appsettings.json
    // 加载应用根目录的 appsettings
    builder.Configuration.SetBasePath(Directory.GetCurrentDirectory());
    builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    #endregion

    // 配置文件根目录，如果是容器，使用位置：/Configurations，否则使用相对位置：./Configurations
    string configurationRootPath = Path.GetFullPath(isContainer ? "/Configurations" : "./Configurations");
    Log.Information($"Configuration root path: {configurationRootPath}");
    if (Directory.Exists(configurationRootPath))
    {
        builder.Configuration.SetBasePath(configurationRootPath);

        // 添加配置文件
        if (jsonFileNames is { Length: > 0 })
        {
            foreach (var fileName in jsonFileNames)
            {
                if (fileName.IsNullOrEmpty())
                    continue;
                builder.Configuration.AddJsonFile($"{fileName}.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"{fileName}.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
            }
        }
    }
    else
    {
        Log.Warning($"Configuration root path not found: {configurationRootPath}");
    }

    // User Secrets
    builder.Configuration.AddUserSecrets<Program>(optional: true);

    // Environment Variables
    builder.Configuration.AddEnvironmentVariables("DOTNET_")
        .AddEnvironmentVariables("ASPNETCORE_")
        .AddEnvironmentVariables("WebStarter_");
}

