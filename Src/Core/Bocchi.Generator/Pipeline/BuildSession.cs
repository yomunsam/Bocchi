using System.Collections.Concurrent;

using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Sinks;
using Bocchi.Workspace.Scanning;

namespace Bocchi.Generator.Pipeline;

/// <summary>
/// 单次构建的可变上下文。所有 Stage 在执行期间向此对象追加日志 / artifact / 派生数据。
/// 由 <see cref="GeneratorPipeline"/> 创建并贯穿整次构建。
/// </summary>
public sealed class BuildSession
{
    private readonly List<BuildLog> _logs = [];
    private readonly List<BuildArtifact> _artifacts = [];
    private readonly ConcurrentDictionary<string, object?> _bag = new(StringComparer.Ordinal);

    public BuildSession(
        Guid sessionId,
        BuildOptions options,
        IBuildSink sink,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sink);
        SessionId = sessionId;
        Options = options;
        Sink = sink;
        StartedAt = startedAt;
        CancellationToken = cancellationToken;
    }

    public Guid SessionId { get; }

    public BuildOptions Options { get; }

    public IBuildSink Sink { get; }

    public DateTimeOffset StartedAt { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary><see cref="Stages.LoadContentStage"/> 写入。</summary>
    public ScanResult? Scan { get; internal set; }

    /// <summary><see cref="Stages.BuildContentGraphStage"/> 写入。</summary>
    public ContentGraph.ContentGraph? Graph { get; internal set; }

    /// <summary><see cref="Stages.ComputeFingerprintStage"/> 写入。</summary>
    public BuildFingerprint? Fingerprint { get; internal set; }

    /// <summary>当 <see cref="Stages.ShortCircuitIfUpToDateStage"/> 命中时为 <c>true</c>。</summary>
    public bool ShortCircuited { get; internal set; }

    /// <summary>当前 ScanRun 在 SQLite 中的 ID（来自 <see cref="ScanResult"/>）。</summary>
    public long? ScanRunId => Scan?.ScanRunId;

    /// <summary>当前 BuildRun 在 SQLite 中的 ID（由 <see cref="Stages.PrepareSessionStage"/> 写入）。</summary>
    public long? BuildRunId { get; internal set; }

    public IReadOnlyList<BuildLog> Logs => _logs;

    public IReadOnlyList<BuildArtifact> Artifacts => _artifacts;

    public void Log(string stage, BuildLogLevel level, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(message);
        _logs.Add(new BuildLog(DateTimeOffset.UtcNow, stage, level, message));
    }

    public void RecordArtifact(BuildArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        _artifacts.Add(artifact);
    }

    public T? GetItem<T>(string key) where T : class
        => _bag.TryGetValue(key, out var v) ? v as T : null;

    public void SetItem(string key, object? value) => _bag[key] = value;
}