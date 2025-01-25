using Bocchi.Home.Infrastructure;
using Bocchi.Home.WebHost.Components;
using Bocchi.Home.WebHost.Extensions;
using Serilog;

namespace Bocchi.Home.WebHost;


/// <summary>
/// 服务注册等
/// </summary>
internal static class WebHostModule
{
    /// <summary>
    /// 配置主机服务
    /// </summary>
    /// <param name="builder"></param>
    public static void ConfigureHost(WebApplicationBuilder builder)
    {
        // 这里主要是把启动Asp net core所需的各种主机服务进行配置，因为都堆在Program.cs看着很乱所以独立出来，类似于以前的Startup.cs
        var services = builder.Services;

        // Razor Pages
        services.AddRazorPages();

        // Blazor Server模式
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // 数据库
        InfrastructureModule.AddBocchiAppDatabase(services, builder.Configuration);
        services.AddDatabaseDeveloperPageExceptionFilter();


        // 配置Antiforgery
        builder.Services.AddAntiforgery();
    }

    


    public static void Configure(WebApplication app)
    {
        // 这里主要是配置各种请求管道、中间件
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
    }


    /// <summary>
    /// 业务逻辑相关的服务注册
    /// </summary>
    /// <param name="services"></param>
    public static void ConfigureServices(IServiceCollection services)
    {
        
    }



    public static void EnsureBocchiConfigurationFile(WebApplicationBuilder builder)
    {
        // 这里要确认配置文件是否存在，如果不存在则创建一个，并写入默认配置
        bool isContainer = builder.Environment.IsContainer();
        string configurationRootPath = Path.GetFullPath(isContainer ? "/Configurations" : "./Configurations");
        Log.Information($"Configuration root path: {configurationRootPath}");
        if (!Directory.Exists(configurationRootPath))
        {
            Directory.CreateDirectory(configurationRootPath);
        }

        var filePath = Path.Combine(configurationRootPath, "Bocchi.json");
        if (!File.Exists(filePath))
        {
            string jsonText = """
            {
                "Bocchi": {
                    
                }
            }
            """;
            File.WriteAllText(filePath, jsonText);
            Log.Information($"Bocchi configuration file created: {filePath}");
        }
        
    }
}