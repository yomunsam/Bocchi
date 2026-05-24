using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services.Git;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Tests;

/// <summary>验证 Git provider 连接的受保护凭据持久化。</summary>
public sealed class GitProviderConnectionServiceTests
{
    /// <summary>保存连接时 token 只进入 Data Protection 保护后的字段，解密读取时保持原 JSON。</summary>
    [Fact]
    public async Task SaveAsync_ProtectsCredentialJson()
    {
        await using var context = await CreateContextAsync();
        var service = new GitProviderConnectionService(context.Db, context.Protection, TimeProvider.System);

        var record = await service.SaveAsync(new GitProviderConnectionSaveInput(
            null,
            GitProviderKeys.GitHub,
            "https://github.com",
            "octocat",
            "read:user repo",
            """{"accessToken":"secret-token","tokenType":"bearer"}"""));

        record.ProtectedCredentialJson.Should().NotContain("secret-token");
        var persisted = await context.Db.GitProviderConnections.SingleAsync();
        persisted.ProtectedCredentialJson.Should().NotContain("secret-token");
        service.UnprotectCredentialJson(persisted).Should().Contain("secret-token");
    }

    /// <summary>创建内存 SQLite 与测试用 Data Protection key ring。</summary>
    private static async Task<TestContext> CreateContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var keyDir = Path.Combine(Path.GetTempPath(), "bocchi-git-connection-tests", Guid.NewGuid().ToString("N"));
        var protection = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
        return new TestContext(connection, db, protection, keyDir);
    }

    /// <summary>测试依赖集合。</summary>
    private sealed class TestContext : IAsyncDisposable
    {
        /// <summary>构造测试上下文。</summary>
        public TestContext(SqliteConnection connection, BocchiDbContext db, IDataProtectionProvider protection, string keyDirectory)
        {
            Connection = connection;
            Db = db;
            Protection = protection;
            KeyDirectory = keyDirectory;
        }

        /// <summary>SQLite 连接。</summary>
        public SqliteConnection Connection { get; }

        /// <summary>测试数据库。</summary>
        public BocchiDbContext Db { get; }

        /// <summary>测试用 Data Protection provider。</summary>
        public IDataProtectionProvider Protection { get; }

        /// <summary>临时 key ring 目录。</summary>
        public string KeyDirectory { get; }

        /// <summary>释放数据库连接和 key ring。</summary>
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
            if (Directory.Exists(KeyDirectory))
            {
                Directory.Delete(KeyDirectory, recursive: true);
            }
        }
    }
}
