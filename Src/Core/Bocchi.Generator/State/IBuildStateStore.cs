using Bocchi.Generator.Pipeline;

namespace Bocchi.Generator.State;

/// <summary>BuildRuns 行的轻量投影，足够 HomeServer 列表页使用。</summary>
public sealed record BuildRunSummary(
    long Id,
    Guid SessionId,
    long? ScanRunId,
    string Mode,
    string Environment,
    string? ThemeId,
    bool IncludeDrafts,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    BuildStatus Status,
    string? Fingerprint,
    string? Reason);

/// <summary>构建状态读写。</summary>
public interface IBuildStateStore
{
    /// <summary>启动一次构建，写入 BuildRuns 行并返回数据库 ID。</summary>
    Task<long> BeginRunAsync(BuildSession session, string? themeId, string? bocchiVersion, CancellationToken cancellationToken);

    /// <summary>追加单条日志到 BuildStageLogs。</summary>
    Task AppendLogAsync(long buildRunId, BuildLog log, CancellationToken cancellationToken);

    /// <summary>登记一个 artifact。</summary>
    Task RecordArtifactAsync(long buildRunId, BuildArtifact artifact, CancellationToken cancellationToken);

    /// <summary>把整次结果落盘。</summary>
    Task CompleteRunAsync(BuildResult result, CancellationToken cancellationToken);

    /// <summary>读取最近一次成功的 BuildRun（按 StartedAt 降序）。</summary>
    Task<BuildRunSummary?> GetLatestSuccessfulRunAsync(CancellationToken cancellationToken);

    /// <summary>列出最近 N 条 BuildRun。</summary>
    Task<IReadOnlyList<BuildRunSummary>> ListRecentRunsAsync(int limit, CancellationToken cancellationToken);
}
