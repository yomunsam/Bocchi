using Bocchi.Home.Core.Data;
using Bocchi.Home.WebHost.Helper.Setup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Bocchi.Home.WebHost.Pages.Setup
{
    public class IndexModel(AppDbContext dbContext)
        : PageModel
    {

        

        /// <summary>
        /// 数据库是否已经准备好
        /// </summary>
        public bool IsDatabaseReady { get; set; } = false;

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
            IsDatabaseReady = SetupHelper.CheckDatabaseReady(dbContext, out var isCreated, out var needMigrate);
            IsDbCreated = isCreated;
            NeedDbMigrate = needMigrate;

            await Task.Yield();

            // if (IsSetup)
            // {
            //     return NotFound();
            // }
            return Page();
        }
    }
}
