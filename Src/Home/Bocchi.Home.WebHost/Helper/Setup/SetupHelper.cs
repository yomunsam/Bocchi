using Bocchi.Home.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bocchi.Home.WebHost.Helper.Setup;

public static class SetupHelper
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="isCreated">数据库是否已创建</param>
    /// <param name="needMigrate">是否需要迁移（包括从未迁移或有更新）</param>
    /// <returns></returns>
    public static async Task<(bool isCreated, bool needMigrate)> CheckDatabaseReadyAsync(AppDbContext dbContext)
    {
        // using var scope = serviceProvider.CreateScope();
        // var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        bool isCreated = dbContext.Database.GetService<IRelationalDatabaseCreator>().Exists();
        bool needMigrate = false;
        if (isCreated)
        {
            needMigrate = (await dbContext.Database.GetPendingMigrationsAsync()).Any();
        }
        

        return (isCreated, needMigrate);
    }
}