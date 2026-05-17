using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bocchi.HomeServer.Data;

/// <summary>
/// EF Core CLI 设计期工厂。迁移生成时不依赖完整 Web Host，避免触发工作区初始化副作用。
/// </summary>
public sealed class BocchiDbContextFactory : IDesignTimeDbContextFactory<BocchiDbContext>
{
    /// <summary>创建用于生成迁移的 DbContext。</summary>
    public BocchiDbContext CreateDbContext(string[] args)
    {
        var dbPath = Path.GetFullPath(Path.Combine("data", "state", "bocchi.sqlite"));
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new BocchiDbContext(options);
    }
}
