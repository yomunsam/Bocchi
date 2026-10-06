using System.IO.Compression;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Hosting;
using Bocchi.HomeServer.Maintenance;
using Bocchi.Workspace;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Tests;

public sealed class DataBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bocchi-backup-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Backup_ContainsDatabaseKeysAndWorkspaceButNotRegenerableData()
    {
        var layout = SeedDataRoot("source", "hello");

        var zipPath = new DataBackupService(layout, TimeProvider.System).CreateBackup();

        zipPath.Should().StartWith(layout.BackupsDirectory);
        using var zip = ZipFile.OpenRead(zipPath);
        zip.Entries.Select(e => e.FullName).Should().Contain(
        [
            DataBackupService.ManifestFileName,
            "state/bocchi.sqlite",
            "keys/key-1.xml",
            "workspace/posts/a.md",
            "themes/custom/theme.json",
        ]).And.NotContain(name =>
            name.StartsWith("cache/", StringComparison.Ordinal)
            || name.StartsWith("logs/", StringComparison.Ordinal)
            || name.StartsWith("output/", StringComparison.Ordinal)
            || name.StartsWith("backups/", StringComparison.Ordinal)
            || name.StartsWith("themes/.backups/", StringComparison.Ordinal)
            || name.EndsWith("-wal", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_IntoEmptyDataRoot_RecreatesFilesAndDatabase()
    {
        var source = SeedDataRoot("source", "hello");
        var zipPath = new DataBackupService(source, TimeProvider.System).CreateBackup(Path.Combine(_root, "b.zip"));
        var target = new BocchiDataLayout(Path.Combine(_root, "target"));

        var safety = new DataBackupService(target, TimeProvider.System).Restore(zipPath, force: false);

        safety.Should().BeNull();
        File.ReadAllText(Path.Combine(target.Workspace.Root, "posts", "a.md")).Should().Be("post");
        File.ReadAllText(Path.Combine(target.DataProtectionKeysDirectory, "key-1.xml")).Should().Be("<key/>");
        ReadMarker(target).Should().Be("hello");
    }

    [Fact]
    public void Restore_OverExistingData_RequiresForceAndKeepsSafetyBackup()
    {
        var source = SeedDataRoot("source", "from-backup");
        var zipPath = new DataBackupService(source, TimeProvider.System).CreateBackup(Path.Combine(_root, "b.zip"));
        var target = SeedDataRoot("target", "current");
        File.WriteAllText(Path.Combine(target.Workspace.Root, "only-in-target.md"), "x");
        var service = new DataBackupService(target, TimeProvider.System);

        var withoutForce = () => service.Restore(zipPath, force: false);
        withoutForce.Should().Throw<InvalidOperationException>().WithMessage("*--force*");

        var safety = service.Restore(zipPath, force: true);

        ReadMarker(target).Should().Be("from-backup");
        File.Exists(Path.Combine(target.Workspace.Root, "only-in-target.md")).Should().BeFalse();
        File.Exists(Path.Combine(target.LogsDirectory, "app.log")).Should().BeTrue("logs 不属于恢复范围");
        File.Exists(Path.Combine(target.PublicOutputDirectory, "index.html")).Should().BeFalse("旧的生成结果不能留到恢复之后");
        File.Exists(Path.Combine(target.CacheDirectory, "x.bin")).Should().BeFalse();
        safety.Should().NotBeNull();
        using var zip = ZipFile.OpenRead(safety!);
        zip.GetEntry("workspace/only-in-target.md").Should().NotBeNull();
    }

    [Fact]
    public void DataRootLock_IsExclusiveUntilReleased()
    {
        var root = Path.Combine(_root, "locked");
        using (var first = DataRootLock.TryAcquire(root))
        {
            first.Should().NotBeNull();
            DataRootLock.TryAcquire(root).Should().BeNull();
        }

        using var again = DataRootLock.TryAcquire(root);
        again.Should().NotBeNull();
    }

    [Fact]
    public void RestoreCommand_RefusesWhileServerHoldsTheLock()
    {
        var source = SeedDataRoot("source", "from-backup");
        var zipPath = new DataBackupService(source, TimeProvider.System).CreateBackup(Path.Combine(_root, "b.zip"));
        var target = SeedDataRoot("target", "current");
        using var serverLock = DataRootLock.TryAcquire(target.DataRoot);
        var output = new StringWriter();

        var exitCode = MaintenanceCli.Run(["restore", zipPath, "--force"], target, output);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("请先停止服务");
        ReadMarker(target).Should().Be("current");
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("workspace/../../escape.txt")]
    [InlineData("logs/injected.log")]
    public void Restore_RejectsUnsafeEntryBeforeTouchingExistingData(string entryName)
    {
        var source = SeedDataRoot("source", "from-backup");
        var zipPath = new DataBackupService(source, TimeProvider.System).CreateBackup(Path.Combine(_root, "b.zip"));
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryName).Open());
            writer.Write("x");
        }

        var target = SeedDataRoot("target", "current");

        var act = () => new DataBackupService(target, TimeProvider.System).Restore(zipPath, force: true);

        act.Should().Throw<InvalidDataException>();
        ReadMarker(target).Should().Be("current");
        File.ReadAllText(Path.Combine(target.Workspace.Root, "posts", "a.md")).Should().Be("post");
        Directory.Exists(target.BackupsDirectory).Should().BeFalse("校验失败时不应生成安全备份");
    }

    [Fact]
    public void Restore_RejectsArchiveWithoutManifest()
    {
        var zipPath = Path.Combine(_root, "foreign.zip");
        Directory.CreateDirectory(_root);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntry("state/bocchi.sqlite");
        }

        var act = () => new DataBackupService(new BocchiDataLayout(Path.Combine(_root, "t")), TimeProvider.System)
            .Restore(zipPath, force: true);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task Migrator_BacksUpExistingDatabaseOnlyWhenMigrationsArePending()
    {
        var layout = new BocchiDataLayout(Path.Combine(_root, "migrate"));
        var migrator = CreateMigrator(layout, retain: 5);

        // 全新安装：数据库文件不存在，不做快照。
        await using (var db = CreateDb(layout))
        {
            File.Delete(layout.SqliteDatabasePath);
            await migrator.MigrateAsync(db);
        }

        Directory.Exists(migrator.SnapshotDirectory).Should().BeFalse();

        // 已是最新：没有待应用 migration，不做快照。
        await using (var db = CreateDb(layout))
        {
            await migrator.MigrateAsync(db);
        }

        Directory.Exists(migrator.SnapshotDirectory).Should().BeFalse();

        // 模拟升级：已有数据库文件，但还有待应用的 migration。
        SqliteConnection.ClearAllPools();
        File.Delete(layout.SqliteDatabasePath);
        WriteMarker(layout.SqliteDatabasePath, "before-upgrade");
        await using (var db = CreateDb(layout))
        {
            await migrator.MigrateAsync(db);
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }

        var snapshot = Directory.GetFiles(migrator.SnapshotDirectory).Should().ContainSingle().Subject;
        snapshot.Should().EndWith(DatabaseMigrator.SnapshotSuffix);
        ReadMarkerFrom(snapshot).Should().Be("before-upgrade");
    }

    [Fact]
    public void Migrator_KeepsOnlyConfiguredNumberOfSnapshots()
    {
        var layout = new BocchiDataLayout(Path.Combine(_root, "prune"));
        var migrator = CreateMigrator(layout, retain: 3);
        Directory.CreateDirectory(migrator.SnapshotDirectory);
        for (var day = 1; day <= 6; day++)
        {
            File.WriteAllText(Path.Combine(migrator.SnapshotDirectory, $"bocchi-2026010{day}T000000Z{DatabaseMigrator.SnapshotSuffix}"), "");
        }

        File.WriteAllText(Path.Combine(migrator.SnapshotDirectory, "keep-me.txt"), "");

        migrator.PruneSnapshots();

        Directory.GetFiles(migrator.SnapshotDirectory).Select(Path.GetFileName).Should().BeEquivalentTo(
        [
            $"bocchi-20260106T000000Z{DatabaseMigrator.SnapshotSuffix}",
            $"bocchi-20260105T000000Z{DatabaseMigrator.SnapshotSuffix}",
            $"bocchi-20260104T000000Z{DatabaseMigrator.SnapshotSuffix}",
            "keep-me.txt",
        ]);
    }

    private BocchiDataLayout SeedDataRoot(string name, string marker)
    {
        var layout = new BocchiDataLayout(Path.Combine(_root, name));
        Directory.CreateDirectory(layout.StateDirectory);
        WriteMarker(layout.SqliteDatabasePath, marker);
        WriteFile(Path.Combine(layout.DataProtectionKeysDirectory, "key-1.xml"), "<key/>");
        WriteFile(Path.Combine(layout.Workspace.Root, "posts", "a.md"), "post");
        WriteFile(Path.Combine(layout.ThemesDirectory, "custom", "theme.json"), "{}");
        WriteFile(Path.Combine(layout.ThemeBackupsDirectory, "old", "theme.json"), "{}");
        WriteFile(Path.Combine(layout.CacheDirectory, "x.bin"), "cache");
        WriteFile(Path.Combine(layout.LogsDirectory, "app.log"), "log");
        WriteFile(Path.Combine(layout.PublicOutputDirectory, "index.html"), "out");
        return layout;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>写一个 WAL 模式的小库（与 EF 建的库一致），里面放一个可识别的标记值。</summary>
    private static void WriteMarker(string databasePath, string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE Marker(Value TEXT); INSERT INTO Marker VALUES ($v);";
        command.Parameters.AddWithValue("$v", marker);
        command.ExecuteNonQuery();
    }

    private static string? ReadMarker(BocchiDataLayout layout) => ReadMarkerFrom(layout.SqliteDatabasePath);

    private static string? ReadMarkerFrom(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Marker";
        return command.ExecuteScalar() as string;
    }

    private static DatabaseMigrator CreateMigrator(BocchiDataLayout layout, int retain)
        => new(layout, TimeProvider.System, Options.Create(new BackupOptions { PreMigrationRetainCount = retain }),
            NullLogger<DatabaseMigrator>.Instance);

    private static BocchiDbContext CreateDb(BocchiDataLayout layout)
    {
        Directory.CreateDirectory(layout.StateDirectory);
        return new BocchiDbContext(new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite($"Data Source={layout.SqliteDatabasePath}")
            .Options);
    }
}
