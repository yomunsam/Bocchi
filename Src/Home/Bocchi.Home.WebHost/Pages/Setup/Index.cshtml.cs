using Bocchi.Home.Core.Data;
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

        /// <summary>
        /// 站点基础设置是否已经准备好
        /// </summary>
        public bool IsSiteSettingReady { get; set; } = false;



        public bool IsSetup => IsDatabaseReady && IsSiteSettingReady;


        public async Task<IActionResult> OnGetAsync()
        {   
            // 检查数据库迁移是否已经完成
            

            if (IsSetup)
            {
                return NotFound();
            }
            return Page();
        }
    }
}
