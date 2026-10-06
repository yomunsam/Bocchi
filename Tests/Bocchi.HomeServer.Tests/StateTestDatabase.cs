using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Tests;

/// <summary>
/// 独立 DataRoot + 已应用 EF Core migration 的 SQLite 文件库，供状态库和扫描器测试使用。
/// 用真实 migration 而不是 EnsureCreated，顺带验证 migration 能从空库建出完整 schema。
/// </summary>
internal sealed class StateTestDatabase : IDbContextFactory<BocchiDbContext>, IDisposable
{
    private readonly DbContextOptions<BocchiDbContext> _options;

    private StateTestDatabase()
    {
        Root = Path.Combine(Path.GetTempPath(), "bocchi-tests", Guid.NewGuid().ToString("N"));
        Layout = new BocchiDataLayout(Root);
        Directory.CreateDirectory(Layout.StateDirectory);
        _options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite($"Data Source={Layout.SqliteDatabasePath}")
            .Options;
    }

    /// <summary>临时 DataRoot 根目录。</summary>
    public string Root { get; }

    /// <summary>临时 DataRoot 布局。</summary>
    public BocchiDataLayout Layout { get; }

    /// <summary>创建并迁移一个新库。</summary>
    public static async Task<StateTestDatabase> CreateAsync()
    {
        var database = new StateTestDatabase();
        await using var db = database.CreateDbContext();
        await db.Database.MigrateAsync();
        return database;
    }

    public BocchiDbContext CreateDbContext() => new(_options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时目录清理失败不影响测试结论。
        }
    }
}
