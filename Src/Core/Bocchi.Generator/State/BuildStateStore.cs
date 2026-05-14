using System.Globalization;

using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Pipeline;
using Bocchi.Workspace.State;

using Microsoft.Data.Sqlite;

namespace Bocchi.Generator.State;

/// <summary>SQLite 实现。表结构由 <see cref="SchemaMigrator"/> v3 维护。</summary>
public sealed class BuildStateStore : IBuildStateStore
{
    private readonly SqliteConnectionFactory _factory;

    public BuildStateStore(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public Task<long> BeginRunAsync(BuildSession session, string? themeId, string? bocchiVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BuildRuns
                  (SessionId, ScanRunId, Mode, Environment, ThemeId, IncludeDrafts, StartedAtUtc, Status, BocchiVersion)
                VALUES
                  ($sid, $scanId, $mode, $env, $theme, $drafts, $started, $status, $version);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$sid", session.SessionId.ToString("D", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$scanId", (object?)session.ScanRunId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mode", session.Options.Mode.ToString());
            cmd.Parameters.AddWithValue("$env", session.Options.Environment);
            cmd.Parameters.AddWithValue("$theme", (object?)themeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drafts", session.Options.IncludeDrafts ? 1 : 0);
            cmd.Parameters.AddWithValue("$started", session.StartedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$status", "Running");
            cmd.Parameters.AddWithValue("$version", (object?)bocchiVersion ?? DBNull.Value);
            var id = (long)(cmd.ExecuteScalar() ?? throw new BuildPipelineException("BuildRuns 写入未能返回 id。"));
            return Task.FromResult(id);
        }
        catch (SqliteException ex)
        {
            throw new BuildPipelineException("写入 BuildRuns 失败。", ex);
        }
    }

    public Task AppendLogAsync(long buildRunId, BuildLog log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BuildStageLogs (BuildRunId, OccurredAtUtc, Stage, Level, Message)
                VALUES ($run, $at, $stage, $level, $msg);
                """;
            cmd.Parameters.AddWithValue("$run", buildRunId);
            cmd.Parameters.AddWithValue("$at", log.OccurredAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$stage", log.Stage);
            cmd.Parameters.AddWithValue("$level", log.Level.ToString());
            cmd.Parameters.AddWithValue("$msg", log.Message);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new BuildPipelineException("写入 BuildStageLogs 失败。", ex);
        }

        return Task.CompletedTask;
    }

    public Task RecordArtifactAsync(long buildRunId, BuildArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO BuildArtifacts (BuildRunId, Path, Kind, ContentType, SizeBytes, Sha256, ProducedBy)
                VALUES ($run, $path, $kind, $ct, $size, $sha, $by);
                """;
            cmd.Parameters.AddWithValue("$run", buildRunId);
            cmd.Parameters.AddWithValue("$path", artifact.Path);
            cmd.Parameters.AddWithValue("$kind", artifact.Kind.ToString());
            cmd.Parameters.AddWithValue("$ct", artifact.ContentType);
            cmd.Parameters.AddWithValue("$size", artifact.SizeBytes);
            cmd.Parameters.AddWithValue("$sha", artifact.Sha256);
            cmd.Parameters.AddWithValue("$by", artifact.ProducedBy);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new BuildPipelineException("写入 BuildArtifacts 失败。", ex);
        }

        return Task.CompletedTask;
    }

    public Task CompleteRunAsync(BuildResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.BuildRunId is null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE BuildRuns
                SET FinishedAtUtc = $fin, Status = $status, Fingerprint = $fp, Reason = $reason
                WHERE Id = $id;
                """;
            cmd.Parameters.AddWithValue("$fin", result.FinishedAt.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$status", result.Status.ToString());
            cmd.Parameters.AddWithValue("$fp", (object?)result.Fingerprint?.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$reason", (object?)result.Reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", result.BuildRunId.Value);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw new BuildPipelineException("更新 BuildRuns 失败。", ex);
        }

        return Task.CompletedTask;
    }

    public Task<BuildRunSummary?> GetLatestSuccessfulRunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, SessionId, ScanRunId, Mode, Environment, ThemeId, IncludeDrafts,
                       StartedAtUtc, FinishedAtUtc, Status, Fingerprint, Reason
                FROM BuildRuns
                WHERE Status = 'Succeeded'
                ORDER BY StartedAtUtc DESC
                LIMIT 1;
                """;
            using var reader = cmd.ExecuteReader();
            return Task.FromResult(reader.Read() ? Map(reader) : null);
        }
        catch (SqliteException ex)
        {
            throw new BuildPipelineException("读取 BuildRuns 失败。", ex);
        }
    }

    public Task<IReadOnlyList<BuildRunSummary>> ListRecentRunsAsync(int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capped = limit <= 0 ? 50 : Math.Min(limit, 500);
        try
        {
            using var conn = _factory.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, SessionId, ScanRunId, Mode, Environment, ThemeId, IncludeDrafts,
                       StartedAtUtc, FinishedAtUtc, Status, Fingerprint, Reason
                FROM BuildRuns
                ORDER BY StartedAtUtc DESC
                LIMIT $limit;
                """;
            cmd.Parameters.AddWithValue("$limit", capped);
            using var reader = cmd.ExecuteReader();
            var list = new List<BuildRunSummary>(capped);
            while (reader.Read())
            {
                var summary = Map(reader);
                if (summary is not null)
                {
                    list.Add(summary);
                }
            }

            return Task.FromResult<IReadOnlyList<BuildRunSummary>>(list);
        }
        catch (SqliteException ex)
        {
            throw new BuildPipelineException("读取 BuildRuns 失败。", ex);
        }
    }

    private static BuildRunSummary? Map(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var sessionId = Guid.Parse(reader.GetString(1));
        long? scanRunId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        var mode = reader.GetString(3);
        var environment = reader.GetString(4);
        var themeId = reader.IsDBNull(5) ? null : reader.GetString(5);
        var includeDrafts = reader.GetInt64(6) != 0;
        var startedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture).ToUniversalTime();
        DateTimeOffset? finishedAt = reader.IsDBNull(8)
            ? null
            : DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture).ToUniversalTime();
        var statusStr = reader.GetString(9);
        if (!Enum.TryParse<BuildStatus>(statusStr, out var status))
        {
            return null;
        }

        var fingerprint = reader.IsDBNull(10) ? null : reader.GetString(10);
        var reason = reader.IsDBNull(11) ? null : reader.GetString(11);
        return new BuildRunSummary(id, sessionId, scanRunId, mode, environment, themeId, includeDrafts,
            startedAt, finishedAt, status, fingerprint, reason);
    }
}