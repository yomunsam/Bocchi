namespace Bocchi.HomeServer.Data;

/// <summary>
/// 单次发布运行记录。它记录 Home Server 从本地静态输出推送到远端目标的结果，
/// 不保存任何 token、密钥或完整响应体。
/// </summary>
public sealed class PublishRunRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>关联的发布方案；方案被删除后保留运行历史。</summary>
    public int? PublishPlanId { get; set; }

    /// <summary>运行当时的发布方案名称快照，避免后续改名影响历史。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>发布渠道稳定 key，例如 <c>github-pages</c>。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>发布时间开始点。</summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>发布时间结束点；运行中时为空。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>发布运行状态。</summary>
    public PublishRunStatus Status { get; set; } = PublishRunStatus.Running;

    /// <summary>关联的 Generator BuildRuns id；构建失败或未开始时可空。</summary>
    public long? BuildRunId { get; set; }

    /// <summary>关联的 Generator session id；便于和高级构建日志对账。</summary>
    public string? BuildSessionId { get; set; }

    /// <summary>构建指纹；远端发布记录可据此确认内容版本。</summary>
    public string? BuildFingerprint { get; set; }

    /// <summary>本次发布看到的静态输出文件数量。</summary>
    public int ArtifactCount { get; set; }

    /// <summary>远端提交 SHA；如果 Pages source 配置失败，仍保留已经完成的 branch commit。</summary>
    public string? RemoteCommitSha { get; set; }

    /// <summary>远端结果 URL，通常是 GitHub Pages URL 或目标 branch URL。</summary>
    public string? RemoteUrl { get; set; }

    /// <summary>失败原因摘要；必须经过脱敏，不能包含凭据。</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>发布运行状态，和构建状态分开，避免把远端发布失败误读成构建失败。</summary>
public enum PublishRunStatus
{
    /// <summary>发布运行已经开始但尚未结束。</summary>
    Running,

    /// <summary>静态输出已经成功推送到目标。</summary>
    Succeeded,

    /// <summary>构建、输出校验或远端发布任一步失败。</summary>
    Failed,
}
