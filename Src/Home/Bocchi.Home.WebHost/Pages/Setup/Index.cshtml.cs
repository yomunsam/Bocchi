using Bocchi.Home.Core.Data;
using Bocchi.Home.WebHost.Helper.Setup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.Home.WebHost.Pages.Setup
{
    public class IndexModel(AppDbContext dbContext, ILogger<IndexModel> logger)
        : PageModel
    {

        

        /// <summary>
        /// 数据库是否已经准备好
        /// </summary>
        public bool IsDatabaseReady => IsDbCreated && !NeedDbMigrate;

        public bool IsDbCreated { get; set; } = false;
        public bool NeedDbMigrate { get; set; } = false;

        /// <summary>
        /// 站点基础设置是否已经准备好
        /// </summary>
        public bool IsSiteSettingReady { get; set; } = true;


        public bool IsSetup => IsDatabaseReady && IsSiteSettingReady;


        public async Task<IActionResult> OnGetAsync()
        {   
            // 检查数据库是否已准备好
            var dbReady = await SetupHelper.CheckDatabaseReadyAsync(dbContext);
            IsDbCreated = dbReady.isCreated;
            NeedDbMigrate = dbReady.needMigrate;


            if (IsSetup)
            {
                return Redirect("~/");
            }
            return Page();
        }

        public async Task<IActionResult> OnPostMigrateAsync()
        {
            // 检查数据库是否已准备好
            var dbReady = await SetupHelper.CheckDatabaseReadyAsync(dbContext);
            IsDbCreated = dbReady.isCreated;
            NeedDbMigrate = dbReady.needMigrate;

            if(!IsDbCreated || NeedDbMigrate)
            {
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Database migrated");
            }

            return Redirect("~/");
        }

    }
}
