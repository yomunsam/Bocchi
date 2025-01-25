using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.Home.Infrastructure;

public static class InfrastructureModule
{
    /// <summary>
    /// Configure services for Infrastructure class library
    /// </summary>
    /// <param name="services"></param>
    public static void ConfigureServices(IServiceCollection services)
    {

    }

    public static void AddBocchiAppDatabase(in IServiceCollection services, in IConfiguration configuration)
    {
        bool usePool = configuration.GetValue<bool>("Database:Bocchi:UseConnectionPool");
    }
}
