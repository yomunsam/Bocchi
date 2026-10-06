using Bocchi.ContentModel;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.State;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

namespace Bocchi.Generator.Tests;

/// <summary>
/// Generator 测试用的内容状态库：持久化实现在 Home Server（EF Core），这里只需要给扫描器一个可写的落点。
/// </summary>
internal sealed class InMemoryContentStateStore : IContentStateStore
{
    private long _nextId;

    public Task<long> StartScanRunAsync(DateTimeOffset startedAt, string? gitHeadSha, CancellationToken cancellationToken = default)
        => Task.FromResult(Interlocked.Increment(ref _nextId));

    public Task FinishScanRunAsync(long scanRunId, DateTimeOffset finishedAt, int filesScanned, int itemsLoaded,
        int errorCount, int warningCount, string status, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<long> UpsertFileAsync(FileUpsert file, CancellationToken cancellationToken = default)
        => Task.FromResult(Interlocked.Increment(ref _nextId));

    public Task UpsertContentItemAsync(ContentItemUpsert item, long fileId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AppendErrorsAsync(long scanRunId, IEnumerable<ContentValidationError> errors, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<ScanRunRecord?> GetLatestScanRunAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ScanRunRecord?>(null);

    public Task<IReadOnlyList<ContentValidationError>> ListErrorsAsync(long scanRunId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ContentValidationError>>([]);

    public Task<IReadOnlyList<ContentSummary>> ListContentSummariesAsync(ContentKind? kind, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ContentSummary>>([]);

    public Task DeleteContentBySourcePathAsync(string relativePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Generator 测试用的构建状态库，保留短路判断和断言需要的构建记录。</summary>
internal sealed class InMemoryBuildStateStore : IBuildStateStore
{
    private readonly List<BuildRunSummary> _runs = [];

    public Task<long> BeginRunAsync(BuildSession session, string? themeId, string? bocchiVersion, CancellationToken cancellationToken)
    {
        lock (_runs)
        {
            var id = _runs.Count + 1L;
            // 运行中的记录先用 Failed 占位，CompleteRunAsync 时覆盖为真实结果。
            _runs.Add(new BuildRunSummary(id, session.SessionId, session.ScanRunId, session.Options.Mode.ToString(),
                session.Options.Environment, themeId, session.Options.IncludeDrafts, session.StartedAt, null,
                BuildStatus.Failed, null, null));
            return Task.FromResult(id);
        }
    }

    public Task AppendLogAsync(long buildRunId, BuildLog log, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RecordArtifactAsync(long buildRunId, BuildArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CompleteRunAsync(BuildResult result, CancellationToken cancellationToken)
    {
        if (result.BuildRunId is { } id)
        {
            lock (_runs)
            {
                var index = (int)id - 1;
                _runs[index] = _runs[index] with
                {
                    FinishedAt = result.FinishedAt,
                    Status = result.Status,
                    Fingerprint = result.Fingerprint?.Value,
                    Reason = result.Reason,
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<BuildRunSummary?> GetLatestSuccessfulRunAsync(CancellationToken cancellationToken)
    {
        lock (_runs)
        {
            return Task.FromResult(_runs.LastOrDefault(x => x.Status == BuildStatus.Succeeded));
        }
    }

    public Task<IReadOnlyList<BuildRunSummary>> ListRecentRunsAsync(int limit, CancellationToken cancellationToken)
    {
        lock (_runs)
        {
            return Task.FromResult<IReadOnlyList<BuildRunSummary>>(_runs.AsEnumerable().Reverse().Take(limit).ToList());
        }
    }
}
