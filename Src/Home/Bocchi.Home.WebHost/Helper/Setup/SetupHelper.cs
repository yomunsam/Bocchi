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
    public static bool CheckDatabaseReady(AppDbContext dbContext, 
        out bool isCreated,
        out bool needMigrate 
        )
    {
        // using var scope = serviceProvider.CreateScope();
        // var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        isCreated = dbContext.Database.GetService<IRelationalDatabaseCreator>().Exists();
        if (isCreated)
        {
            needMigrate = dbContext.Database.GetPendingMigrations().Any();
        }
        else
        {
            needMigrate = true;
        }

        return isCreated && !needMigrate;
    }
}