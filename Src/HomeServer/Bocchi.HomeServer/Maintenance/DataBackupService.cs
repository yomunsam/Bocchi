using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

using Bocchi.Workspace;

using Microsoft.Data.Sqlite;

namespace Bocchi.HomeServer.Maintenance;

/// <summary>
/// DataRoot 备份与恢复。备份是一个 zip：数据库一致性快照 + Data Protection 密钥 + workspace + 已安装 Theme
/// 以及 state 下的其他文件；cache、logs、output、backups 都可再生或属于备份本身，不打包。
/// </summary>
public sealed class DataBackupService
{
    /// <summary>zip 内的清单文件名，恢复时用它确认这是 Bocchi 备份。</summary>
    public const string ManifestFileName = "bocchi-backup.json";

    /// <summary>当前备份格式版本。</summary>
    public const int FormatVersion = 1;

    /// <summary>不进入备份的顶层目录和文件（含服务运行时的锁文件）。</summary>
    private static readonly string[] ExcludedTopLevel = ["backups", "cache", "logs", "output", Hosting.DataRootLock.FileName];

    private readonly BocchiDataLayout _layout;
    private readonly TimeProvider _time;

    /// <summary>创建备份服务。</summary>
    public DataBackupService(BocchiDataLayout layout, TimeProvider time)
    {
        _layout = layout;
        _time = time;
    }

    /// <summary>
    /// 创建完整备份。服务运行中也可以执行：数据库走 SQLite online backup，拿到的是一致快照。
    /// </summary>
    /// <param name="outputPath">zip 输出路径；为空时写到 <c>&lt;data&gt;/backups/</c>。</param>
    /// <returns>实际写出的 zip 路径。</returns>
    public string CreateBackup(string? outputPath = null)
    {
        var target = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(_layout.BackupsDirectory, $"bocchi-backup-{Stamp()}.zip")
            : Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var snapshot = Path.Combine(Path.GetTempPath(), $"bocchi-db-{Guid.NewGuid():N}.sqlite");
        try
        {
            var hasDatabase = File.Exists(_layout.SqliteDatabasePath);
            if (hasDatabase)
            {
                SnapshotDatabase(_layout.SqliteDatabasePath, snapshot);
            }

            using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
            WriteManifest(zip);
            if (hasDatabase)
            {
                zip.CreateEntryFromFile(snapshot, ToEntryName(_layout.SqliteDatabasePath), CompressionLevel.Optimal);
            }

            foreach (var file in EnumerateBackupFiles())
            {
                zip.CreateEntryFromFile(file, ToEntryName(file), CompressionLevel.Optimal);
            }
        }
        finally
        {
            File.Delete(snapshot);
        }

        return target;
    }

    /// <summary>
    /// 从备份恢复。必须在服务停止时执行（调用方负责先拿到 <see cref="Hosting.DataRootLock"/>）。DataRoot 已有数据时需要 <paramref name="force"/>：
    /// 先把现有数据另做一份备份，再清空（保留 backups/logs）后解压。
    /// </summary>
    /// <returns>强制恢复前自动生成的备份路径；DataRoot 原本为空时为 <c>null</c>。</returns>
    public string? Restore(string backupPath, bool force)
    {
        var source = Path.GetFullPath(backupPath);
        using var zip = ZipFile.OpenRead(source);
        ValidateManifest(zip);

        string? safetyBackup = null;
        if (HasRestorableData())
        {
            if (!force)
            {
                throw new InvalidOperationException(
                    $"数据目录 {_layout.DataRoot} 里已有数据。确认要覆盖时加 --force（会先自动备份现有数据）。");
            }

            safetyBackup = CreateBackup(Path.Combine(_layout.BackupsDirectory, $"bocchi-before-restore-{Stamp()}.zip"));
            ClearRestorableData();
        }

        var root = Path.GetFullPath(_layout.DataRoot) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName == ManifestFileName || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"备份中的路径 '{entry.FullName}' 越出了数据目录。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        return safetyBackup;
    }

    /// <summary>
    /// 用 SQLite online backup 把数据库复制成一个独立文件（包含 WAL 中尚未 checkpoint 的数据）。
    /// </summary>
    public static void SnapshotDatabase(string databasePath, string destinationPath)
    {
        using (var source = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
        using (var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
        }
    }

    /// <summary>备份文件名使用的 UTC 时间戳。</summary>
    public string Stamp()
        => _time.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private IEnumerable<string> EnumerateBackupFiles()
    {
        if (!Directory.Exists(_layout.DataRoot))
        {
            yield break;
        }

        var databaseFiles = new[] { "", "-wal", "-shm", "-journal" }
            .Select(suffix => _layout.SqliteDatabasePath + suffix)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_layout.DataRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_layout.DataRoot, file);
            var top = relative.Split(Path.DirectorySeparatorChar)[0];
            if (ExcludedTopLevel.Contains(top, StringComparer.Ordinal)
                || databaseFiles.Contains(file)
                || Path.GetFullPath(file).StartsWith(_layout.ThemeBackupsDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private bool HasRestorableData()
        => Directory.Exists(_layout.DataRoot)
            && Directory.EnumerateFileSystemEntries(_layout.DataRoot)
                .Any(path => !ExcludedTopLevel.Contains(Path.GetFileName(path), StringComparer.Ordinal));

    private void ClearRestorableData()
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(_layout.DataRoot))
        {
            if (ExcludedTopLevel.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
    }

    private void WriteManifest(ZipArchive zip)
    {
        var entry = zip.CreateEntry(ManifestFileName);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, new BackupManifest(FormatVersion, _time.GetUtcNow(), ServerInfo.Version));
    }

    private static void ValidateManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestFileName)
            ?? throw new InvalidDataException("这不是 Bocchi 备份文件（缺少 bocchi-backup.json）。");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(stream);
        if (manifest?.Format != FormatVersion)
        {
            throw new InvalidDataException($"不支持的备份格式版本：{manifest?.Format}。");
        }
    }

    private string ToEntryName(string file)
        => Path.GetRelativePath(_layout.DataRoot, file).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>zip 内清单。</summary>
    private sealed record BackupManifest(int Format, DateTimeOffset CreatedAt, string BocchiVersion);
}
