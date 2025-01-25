using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nekonya;
using Microsoft.EntityFrameworkCore.Sqlite;
using Bocchi.Home.Core.Data;
using Microsoft.EntityFrameworkCore;

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
        int poolSize = configuration.GetValue<int>("Database:Bocchi:PoolSize", 0);
        var (dbProvider, connectionString) = GetConnectionString(configuration, out string confKey);
        if (dbProvider is DatabaseProvider.Unknown || connectionString is null)
        {
            throw new InvalidOperationException($"Invalid database provider or connection string, configuration key: {confKey}");
        }

        if (usePool && poolSize > 0)
        {
            services.AddDbContextPool<AppDbContext>(options =>{
                switch (dbProvider)
                {
                    case DatabaseProvider.Sqlite:
                        options.UseSqlite(connectionString);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported database provider: {dbProvider}");
                }
            }, poolSize);
        }
        else
        {
            services.AddDbContext<AppDbContext>(options =>{
                switch (dbProvider)
                {
                    case DatabaseProvider.Sqlite:
                        options.UseSqlite(connectionString);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported database provider: {dbProvider}");
                }
            });
        }        
    }


    private static (DatabaseProvider, string?) GetConnectionString(IConfiguration configuration, out string confKey)
    {
        var providerTypeStr = configuration.GetValue<string>("Database:Bocchi:Type");
        DatabaseProvider dbProvider = providerTypeStr?.ToLower() switch
        {
            "sqlite" or "sqlite3" => DatabaseProvider.Sqlite,
            "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
            "postgresql" => DatabaseProvider.PostgreSql,
            "mysql" or "mariadb" => DatabaseProvider.MySql,
            _ => DatabaseProvider.Unknown
        };
        
        confKey = dbProvider is not DatabaseProvider.Unknown ? $"Database:Bocchi:ConnectionString:{dbProvider}" : string.Empty;
        return (dbProvider, configuration.GetValue<string>(confKey));
    }


    private enum DatabaseProvider
    {
        Unknown,
     
        Sqlite,
        SqlServer,
        PostgreSql,
        MySql
    }
    
}
