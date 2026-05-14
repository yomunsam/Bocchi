using System.Globalization;

using Bocchi.ContentModel;
using Bocchi.Workspace.Exceptions;
using Bocchi.Workspace.Scanning;

using Microsoft.Data.Sqlite;

namespace Bocchi.Workspace.State;

/// <summary>
/// <see cref="IContentStateStore"/> 的 SQLite 实现。所有写入走事务；不复制内容正文。
/// </summary>
public sealed class ContentStateStore : IContentStateStore
{
    private readonly SqliteConnectionFactory _factory;

    public ContentStateStore(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public Task<long> StartScanRunAsync(DateTimeOffset startedAt, string? gitHeadSha, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ScanRuns (StartedAtUtc, FinishedAtUtc, FilesScanned, ItemsLoaded, ErrorCount, WarningCount, GitHeadSha, Status)
                VALUES ($started, NULL, 0, 0, 0, 0, $head, 'running');
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$started", FormatUtc(startedAt));
            cmd.Parameters.AddWithValue("$head", (object?)gitHeadSha ?? DBNull.Value);
            var id = (long)cmd.ExecuteScalar()!;
            return Task.FromResult(id);
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException("无法开启扫描运行。", ex);
        }
    }

    public Task FinishScanRunAsync(
        long scanRunId, DateTimeOffset finishedAt, int filesScanned, int itemsLoaded,
        int errorCount, int warningCount, string status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE ScanRuns
                SET FinishedAtUtc = $finished, FilesScanned = $files, ItemsLoaded = $items,
                    ErrorCount = $errors, WarningCount = $warnings, Status = $status
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$finished", FormatUtc(finishedAt));
            cmd.Parameters.AddWithValue("$files", filesScanned);
            cmd.Parameters.AddWithValue("$items", itemsLoaded);
            cmd.Parameters.AddWithValue("$errors", errorCount);
            cmd.Parameters.AddWithValue("$warnings", warningCount);
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$id", scanRunId);
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException($"无法结束扫描运行 {scanRunId}。", ex);
        }
    }

    public Task<long> UpsertFileAsync(FileUpsert file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Files (RelativePath, Kind, Sha256, LastModifiedUtc, LastSeenUtc)
                VALUES ($path, $kind, $sha, $modified, $seen)
                ON CONFLICT(RelativePath) DO UPDATE SET
                    Kind = excluded.Kind,
                    Sha256 = excluded.Sha256,
                    LastModifiedUtc = excluded.LastModifiedUtc,
                    LastSeenUtc = excluded.LastSeenUtc;
                SELECT Id FROM Files WHERE RelativePath = $path;
                """;
            cmd.Parameters.AddWithValue("$path", file.RelativePath);
            cmd.Parameters.AddWithValue("$kind", (int)file.Kind);
            cmd.Parameters.AddWithValue("$sha", file.Sha256);
            cmd.Parameters.AddWithValue("$modified", FormatUtc(file.LastModifiedUtc));
            cmd.Parameters.AddWithValue("$seen", FormatUtc(DateTimeOffset.UtcNow));
            var id = (long)cmd.ExecuteScalar()!;
            return Task.FromResult(id);
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException($"无法 upsert 文件记录 '{file.RelativePath}'。", ex);
        }
    }

    public Task UpsertContentItemAsync(ContentItemUpsert item, long? fileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ContentItems (FileId, Kind, ContentId, Slug, Title, Status, Year, PublishedAtUtc, UpdatedAtUtc, FrontmatterJson, LastSeenUtc)
                VALUES ($file, $kind, $cid, $slug, $title, $status, $year, $published, $updated, $fm, $seen)
                ON CONFLICT(Kind, ContentId) DO UPDATE SET
                    FileId = excluded.FileId,
                    Slug = excluded.Slug,
                    Title = excluded.Title,
                    Status = excluded.Status,
                    Year = excluded.Year,
                    PublishedAtUtc = excluded.PublishedAtUtc,
                    UpdatedAtUtc = excluded.UpdatedAtUtc,
                    FrontmatterJson = excluded.FrontmatterJson,
                    LastSeenUtc = excluded.LastSeenUtc;
                """;
            cmd.Parameters.AddWithValue("$file", (object?)fileId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$kind", (int)item.Kind);
            cmd.Parameters.AddWithValue("$cid", item.ContentId);
            cmd.Parameters.AddWithValue("$slug", (object?)item.Slug ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$title", (object?)item.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (int)item.Status);
            cmd.Parameters.AddWithValue("$year", (object?)item.Year ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$published", item.PublishedAt is null ? DBNull.Value : FormatUtc(item.PublishedAt.Value));
            cmd.Parameters.AddWithValue("$updated", item.UpdatedAt is null ? DBNull.Value : FormatUtc(item.UpdatedAt.Value));
            cmd.Parameters.AddWithValue("$fm", (object?)item.FrontmatterJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$seen", FormatUtc(DateTimeOffset.UtcNow));
            cmd.ExecuteNonQuery();
            return Task.CompletedTask;
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException($"无法 upsert 内容项 {item.Kind}/{item.ContentId}。", ex);
        }
    }

    public Task AppendErrorsAsync(long scanRunId, IEnumerable<ContentValidationError> errors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(errors);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ContentErrors (ScanRunId, RelativePath, Kind, Field, Severity, Code, Message)
                VALUES ($run, $path, $kind, $field, $sev, $code, $msg);
                """;
            var pRun = cmd.Parameters.Add("$run", SqliteType.Integer);
            var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
            var pKind = cmd.Parameters.Add("$kind", SqliteType.Integer);
            var pField = cmd.Parameters.Add("$field", SqliteType.Text);
            var pSev = cmd.Parameters.Add("$sev", SqliteType.Integer);
            var pCode = cmd.Parameters.Add("$code", SqliteType.Text);
            var pMsg = cmd.Parameters.Add("$msg", SqliteType.Text);

            foreach (var e in errors)
            {
                pRun.Value = scanRunId;
                pPath.Value = e.RelativePath;
                pKind.Value = e.Kind is null ? DBNull.Value : (int)e.Kind.Value;
                pField.Value = (object?)e.Field ?? DBNull.Value;
                pSev.Value = (int)e.Severity;
                pCode.Value = e.Code;
                pMsg.Value = e.Message;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.CompletedTask;
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException($"无法批量写入扫描错误 (run={scanRunId})。", ex);
        }
    }

    public Task<ScanRunRecord?> GetLatestScanRunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, StartedAtUtc, FinishedAtUtc, FilesScanned, ItemsLoaded, ErrorCount, WarningCount, GitHeadSha, Status
                FROM ScanRuns
                ORDER BY Id DESC LIMIT 1;
                """;
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult<ScanRunRecord?>(null);
            }

            var record = new ScanRunRecord(
                reader.GetInt64(0),
                ParseUtc(reader.GetString(1)),
                reader.IsDBNull(2) ? null : ParseUtc(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8));
            return Task.FromResult<ScanRunRecord?>(record);
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException("无法读取最近一次扫描运行。", ex);
        }
    }

    public Task<IReadOnlyList<ContentValidationError>> ListErrorsAsync(long scanRunId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT RelativePath, Kind, Field, Severity, Code, Message
                FROM ContentErrors
                WHERE ScanRunId = $id
                ORDER BY Severity DESC, Id ASC;
                """;
            cmd.Parameters.AddWithValue("$id", scanRunId);
            using var reader = cmd.ExecuteReader();
            var list = new List<ContentValidationError>();
            while (reader.Read())
            {
                list.Add(new ContentValidationError(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : (ContentKind)reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    (ContentErrorSeverity)reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }

            return Task.FromResult<IReadOnlyList<ContentValidationError>>(list);
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException($"无法读取扫描错误 (run={scanRunId})。", ex);
        }
    }

    public Task<IReadOnlyList<ContentSummary>> ListContentSummariesAsync(ContentKind? kind, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            if (kind is null)
            {
                cmd.CommandText = """
                    SELECT ci.Kind, ci.ContentId, ci.Title, ci.Status, ci.Year, ci.PublishedAtUtc, ci.UpdatedAtUtc, COALESCE(f.RelativePath, '')
                    FROM ContentItems ci LEFT JOIN Files f ON ci.FileId = f.Id
                    ORDER BY ci.Kind ASC, ci.PublishedAtUtc DESC NULLS LAST;
                    """;
            }
            else
            {
                cmd.CommandText = """
                    SELECT ci.Kind, ci.ContentId, ci.Title, ci.Status, ci.Year, ci.PublishedAtUtc, ci.UpdatedAtUtc, COALESCE(f.RelativePath, '')
                    FROM ContentItems ci LEFT JOIN Files f ON ci.FileId = f.Id
                    WHERE ci.Kind = $kind
                    ORDER BY ci.PublishedAtUtc DESC NULLS LAST;
                    """;
                cmd.Parameters.AddWithValue("$kind", (int)kind.Value);
            }

            using var reader = cmd.ExecuteReader();
            var list = new List<ContentSummary>();
            while (reader.Read())
            {
                list.Add(new ContentSummary(
                    (ContentKind)reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    (ContentStatus)reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : ParseUtc(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : ParseUtc(reader.GetString(6)),
                    reader.GetString(7)));
            }

            return Task.FromResult<IReadOnlyList<ContentSummary>>(list);
        }
        catch (SqliteException ex)
        {
            throw new ContentStateException("无法读取内容摘要。", ex);
        }
    }

    private static string FormatUtc(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseUtc(string raw)
        => DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}