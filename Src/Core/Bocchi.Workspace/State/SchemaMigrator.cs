using Bocchi.Workspace.Exceptions;
using Microsoft.Data.Sqlite;

namespace Bocchi.Workspace.State;

/// <summary>
/// SQLite schema 显式迁移器。基于 <c>PRAGMA user_version</c>。
/// 历史：v1 → M2 内容扫描相关表；v2 预留；v3 → M3 构建记录表。
/// </summary>
public sealed class SchemaMigrator
{
    private readonly SqliteConnectionFactory _factory;
    public const int CurrentVersion = 3;

    public SchemaMigrator(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var connection = _factory.OpenConnection();
            var current = ReadUserVersion(connection);
            if (current >= CurrentVersion)
            {
                return Task.FromResult(current);
            }

            using var tx = connection.BeginTransaction();
            if (current < 1)
            {
                ApplyV1(connection, tx);
            }

            if (current < 3)
            {
                ApplyV3(connection, tx);
            }

            SetUserVersion(connection, tx, CurrentVersion);
            tx.Commit();
            return Task.FromResult(CurrentVersion);
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException("SQLite schema 迁移失败。", ex);
        }
    }

    private static int ReadUserVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var result = cmd.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void SetUserVersion(SqliteConnection conn, SqliteTransaction tx, int version)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // user_version doesn't accept parameters; safe to inline an int.
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    private static void ApplyV1(SqliteConnection conn, SqliteTransaction tx)
    {
        const string sql = """
            CREATE TABLE Files (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RelativePath TEXT NOT NULL UNIQUE,
                Kind INTEGER NOT NULL,
                Sha256 TEXT NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL
            );

            CREATE INDEX IX_Files_Kind ON Files(Kind);

            CREATE TABLE ContentItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileId INTEGER NULL REFERENCES Files(Id) ON DELETE SET NULL,
                Kind INTEGER NOT NULL,
                ContentId TEXT NOT NULL,
                Slug TEXT NULL,
                Title TEXT NULL,
                Status INTEGER NOT NULL,
                Year TEXT NULL,
                PublishedAtUtc TEXT NULL,
                UpdatedAtUtc TEXT NULL,
                FrontmatterJson TEXT NULL,
                LastSeenUtc TEXT NOT NULL,
                UNIQUE(Kind, ContentId)
            );

            CREATE INDEX IX_ContentItems_Kind_PublishedAt ON ContentItems(Kind, PublishedAtUtc);

            CREATE TABLE MediaReferences (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ContentItemId INTEGER NOT NULL REFERENCES ContentItems(Id) ON DELETE CASCADE,
                RelativePathFromOwner TEXT NOT NULL,
                ResolvedRelativePath TEXT NOT NULL,
                Exists_ INTEGER NOT NULL
            );

            CREATE INDEX IX_MediaReferences_Item ON MediaReferences(ContentItemId);

            CREATE TABLE ScanRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAtUtc TEXT NOT NULL,
                FinishedAtUtc TEXT NULL,
                FilesScanned INTEGER NOT NULL DEFAULT 0,
                ItemsLoaded INTEGER NOT NULL DEFAULT 0,
                ErrorCount INTEGER NOT NULL DEFAULT 0,
                WarningCount INTEGER NOT NULL DEFAULT 0,
                GitHeadSha TEXT NULL,
                Status TEXT NOT NULL
            );

            CREATE TABLE ContentErrors (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ScanRunId INTEGER NOT NULL REFERENCES ScanRuns(Id) ON DELETE CASCADE,
                RelativePath TEXT NOT NULL,
                Kind INTEGER NULL,
                Field TEXT NULL,
                Severity INTEGER NOT NULL,
                Code TEXT NOT NULL,
                Message TEXT NOT NULL
            );

            CREATE INDEX IX_ContentErrors_Run ON ContentErrors(ScanRunId);
            CREATE INDEX IX_ContentErrors_Severity ON ContentErrors(Severity);
            """;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void ApplyV3(SqliteConnection conn, SqliteTransaction tx)
    {
        const string sql = """
            CREATE TABLE BuildRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL UNIQUE,
                ScanRunId INTEGER NULL REFERENCES ScanRuns(Id) ON DELETE SET NULL,
                Mode TEXT NOT NULL,
                Environment TEXT NOT NULL,
                ThemeId TEXT NULL,
                IncludeDrafts INTEGER NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                FinishedAtUtc TEXT NULL,
                Status TEXT NOT NULL,
                Fingerprint TEXT NULL,
                Reason TEXT NULL,
                BocchiVersion TEXT NULL
            );

            CREATE INDEX IX_BuildRuns_StartedAt ON BuildRuns(StartedAtUtc DESC);

            CREATE TABLE BuildArtifacts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BuildRunId INTEGER NOT NULL REFERENCES BuildRuns(Id) ON DELETE CASCADE,
                Path TEXT NOT NULL,
                Kind TEXT NOT NULL,
                ContentType TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                Sha256 TEXT NOT NULL,
                ProducedBy TEXT NOT NULL
            );

            CREATE INDEX IX_BuildArtifacts_Run ON BuildArtifacts(BuildRunId);
            CREATE INDEX IX_BuildArtifacts_Path ON BuildArtifacts(Path);

            CREATE TABLE BuildStageLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BuildRunId INTEGER NOT NULL REFERENCES BuildRuns(Id) ON DELETE CASCADE,
                OccurredAtUtc TEXT NOT NULL,
                Stage TEXT NOT NULL,
                Level TEXT NOT NULL,
                Message TEXT NOT NULL
            );

            CREATE INDEX IX_BuildStageLogs_Run ON BuildStageLogs(BuildRunId);
            CREATE INDEX IX_BuildStageLogs_Level ON BuildStageLogs(Level);
            """;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
