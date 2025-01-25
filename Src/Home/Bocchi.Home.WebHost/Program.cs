using Bocchi.Home.Core;
using Bocchi.Home.WebHost;
using Bocchi.Home.WebHost.Components;
using Bocchi.Home.WebHost.Extensions;
using Bocchi.Home.Infrastructure;
using Nekonya;
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
    WebHostModule.EnsureBocchiConfigurationFile(builder);
    BuildConfiguration(builder, [
        "Serilog",
        "Database",
        "Bocchi",
    ]);

    // Configuration (Command Line)
    if (args is { Length: > 0 })
    {
        builder.Configuration.AddCommandLine(args);
    }

    // Serilog
    builder.Host.UseSerilog((hostContext, loggerCfg) =>{
        // 检查如果配置文件中有Serilog相关配置，则使用配置文件中的配置
        if (hostContext.Configuration.GetSection("Serilog").Exists())
        {
            loggerCfg.ReadFrom.Configuration(hostContext.Configuration);
        }
        else
        {
            loggerCfg.WriteTo.Console();
            Log.Warning("Serilog configuration not found, use default configuration");
        }
    });

    // 配置主机服务
    WebHostModule.ConfigureHost(builder);
    WebHostModule.ConfigureServices(builder.Services);

    CoreModule.ConfigureServices(builder.Services);
    InfrastructureModule.ConfigureServices(builder.Services);


    // Build and run
    var app = builder.Build();

    // 各种请求管道、中间件配置
    WebHostModule.Configure(app);

    app.Run();
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

