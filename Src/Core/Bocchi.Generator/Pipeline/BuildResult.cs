namespace Bocchi.Generator.Pipeline;

/// <summary>构建结果状态。</summary>
public enum BuildStatus
{
    Succeeded,
    Failed,
    /// <summary>同指纹 → 整次跳过。M3 仅在 <see cref="BuildMode.FullBuild"/> 下可能出现。</summary>
    Skipped,
}

/// <summary>构建结果。</summary>
public sealed record BuildResult
{
    public required Guid SessionId { get; init; }

    public required BuildMode Mode { get; init; }

    public required BuildStatus Status { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset FinishedAt { get; init; }

    public BuildFingerprint? Fingerprint { get; init; }

    public string? Reason { get; init; }

    public required IReadOnlyList<BuildLog> Logs { get; init; }

    public required IReadOnlyList<BuildArtifact> Artifacts { get; init; }

    public long? BuildRunId { get; init; }

    public TimeSpan Duration => FinishedAt - StartedAt;
}