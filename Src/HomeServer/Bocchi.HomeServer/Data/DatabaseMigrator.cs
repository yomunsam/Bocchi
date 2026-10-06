using Bocchi.HomeServer.Hosting;
using Bocchi.HomeServer.Maintenance;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Data;

/// <summary>
/// 应用 EF Core migration。已有数据库且存在待应用的 migration 时（即升级），先把数据库快照到
/// <c>&lt;data&gt;/backups/database/</c>，再执行迁移；快照只保留最近若干份。
/// </summary>
public sealed class DatabaseMigrator
{
    /// <summary>迁移前快照文件名后缀，清理时只匹配这一类文件。</summary>
    public const string SnapshotSuffix = "-pre-migration.sqlite";

    private readonly BocchiDataLayout _layout;
    private readonly TimeProvider _time;
    private readonly IOptions<BackupOptions> _options;
    private readonly ILogger<DatabaseMigrator> _logger;

    /// <summary>创建迁移器。</summary>
    public DatabaseMigrator(BocchiDataLayout layout, TimeProvider time, IOptions<BackupOptions> options, ILogger<DatabaseMigrator> logger)
    {
        _layout = layout;
        _time = time;
        _options = options;
        _logger = logger;
    }

    /// <summary>迁移前快照目录。</summary>
    public string SnapshotDirectory => Path.Combine(_layout.BackupsDirectory, "database");

    /// <summary>按需备份后应用全部待执行 migration。</summary>
    public async Task MigrateAsync(BocchiDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        Directory.CreateDirectory(_layout.StateDirectory);

        // 必须在任何 EF 查询之前判断：打开 SQLite 连接会顺手创建空文件。
        var databaseExisted = File.Exists(_layout.SqliteDatabasePath);
        if (databaseExisted)
        {
            await EnsureNoUnknownMigrationsAsync(db, cancellationToken).ConfigureAwait(false);
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        if (databaseExisted)
        {
            var snapshot = Path.Combine(SnapshotDirectory, $"bocchi-{new DataBackupService(_layout, _time).Stamp()}{SnapshotSuffix}");
            Directory.CreateDirectory(SnapshotDirectory);
            DataBackupService.SnapshotDatabase(_layout.SqliteDatabasePath, snapshot);
            _logger.LogInformation("Backed up database to {Snapshot} before applying migrations {Migrations}", snapshot, pending);
            PruneSnapshots();
        }

        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 库里有本程序不认识的 migration，说明数据库来自更新的版本（或新版本的备份）。
    /// 继续运行只会在陌生的表结构上随机出错，所以直接拒绝启动。
    /// </summary>
    private async Task EnsureNoUnknownMigrationsAsync(BocchiDbContext db, CancellationToken cancellationToken)
    {
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var unknown = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false))
            .Where(id => !known.Contains(id))
            .ToList();
        if (unknown.Count > 0)
        {
            throw new BocchiStartupException(
                $"数据库 {_layout.SqliteDatabasePath} 由更新版本的 Bocchi 创建（未知迁移：{string.Join(", ", unknown)}）。"
                + $"请使用更新的版本，或用 {SnapshotDirectory} 里升级前的快照回退。");
        }
    }

    /// <summary>只保留最近 N 份迁移前快照（按文件名中的时间戳排序）。</summary>
    internal void PruneSnapshots()
    {
        var keep = Math.Max(1, _options.Value.PreMigrationRetainCount);
        foreach (var stale in Directory.EnumerateFiles(SnapshotDirectory, "bocchi-*" + SnapshotSuffix)
                     .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
                     .Skip(keep))
        {
            File.Delete(stale);
        }
    }
}
