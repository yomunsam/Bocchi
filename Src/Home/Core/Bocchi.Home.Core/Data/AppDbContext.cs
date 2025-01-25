using Microsoft.EntityFrameworkCore;

namespace Bocchi.Home.Core.Data;

/// <summary>
/// Bocchi Home 主数据库上下文
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }
}
