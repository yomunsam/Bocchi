using Bocchi.Home.Core.Entities.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.Home.Core.Data;

/// <summary>
/// Bocchi Home 主数据库上下文
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) 
    : IdentityDbContext<BocchiUserEntity, BocchiRoleIdentity, Guid>(options)
{

}
