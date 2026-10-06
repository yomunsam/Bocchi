using Bocchi.Generator.Pipeline;
using Bocchi.Generator.State;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Data.State;

/// <summary>
/// <see cref="IBuildStateStore"/> 的 EF Core 实现。Generator 流水线是 singleton/transient 混合，
/// 每次调用通过 <see cref="IDbContextFactory{TContext}"/> 创建独立 DbContext。
/// </summary>
public sealed class BuildStateStore : IBuildStateStore
{
    private readonly IDbContextFactory<BocchiDbContext> _dbFactory;

    /// <summary>创建构建状态库。</summary>
    public BuildStateStore(IDbContextFactory<BocchiDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<long> BeginRunAsync(BuildSession session, string? themeId, string? bocchiVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var run = new BuildRunRecord
        {
            SessionId = session.SessionId,
            ScanRunId = session.ScanRunId,
            Mode = session.Options.Mode.ToString(),
            Environment = session.Options.Environment,
            ThemeId = themeId,
            IncludeDrafts = session.Options.IncludeDrafts,
            StartedAt = session.StartedAt,
            BocchiVersion = bocchiVersion,
        };
        db.BuildRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return run.Id;
    }

    public async Task AppendLogAsync(long buildRunId, BuildLog log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.BuildStageLogs.Add(new BuildStageLogRecord
        {
            BuildRunId = buildRunId,
            OccurredAt = log.OccurredAt,
            Stage = log.Stage,
            Level = log.Level.ToString(),
            Message = log.Message,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordArtifactAsync(long buildRunId, BuildArtifact artifact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.BuildArtifacts.Add(new BuildArtifactRecord
        {
            BuildRunId = buildRunId,
            Path = artifact.Path,
            Kind = artifact.Kind.ToString(),
            ContentType = artifact.ContentType,
            SizeBytes = artifact.SizeBytes,
            Sha256 = artifact.Sha256,
            ProducedBy = artifact.ProducedBy,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteRunAsync(BuildResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.BuildRunId is not { } id)
        {
            return;
        }

        BuildStatus? status = result.Status;
        var fingerprint = result.Fingerprint?.Value;
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.BuildRuns
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.FinishedAt, result.FinishedAt)
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Fingerprint, fingerprint)
                .SetProperty(x => x.Reason, result.Reason), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<BuildRunSummary?> GetLatestSuccessfulRunAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ProjectSummaries(db.BuildRuns.Where(x => x.Status == BuildStatus.Succeeded))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BuildRunSummary>> ListRecentRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var capped = limit <= 0 ? 50 : Math.Min(limit, 500);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // 运行中的构建（Status 为空）不进入历史列表。
        return await ProjectSummaries(db.BuildRuns.Where(x => x.Status != null))
            .Take(capped)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>按开始时间倒序投影为列表摘要。</summary>
    private static IQueryable<BuildRunSummary> ProjectSummaries(IQueryable<BuildRunRecord> query)
        => query
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Select(x => new BuildRunSummary(
                x.Id, x.SessionId, x.ScanRunId, x.Mode, x.Environment, x.ThemeId, x.IncludeDrafts,
                x.StartedAt, x.FinishedAt, x.Status!.Value, x.Fingerprint, x.Reason));
}
