using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>发布执行服务返回给 UI 和 HTTP endpoint 的脱敏摘要。</summary>
public sealed record PublishExecutionResult
{
    /// <summary>发布运行数据库 id；没有持久化时为 null。</summary>
    public long? PublishRunId { get; init; }

    /// <summary>发布运行状态。</summary>
    public required PublishRunStatus Status { get; init; }

    /// <summary>发布渠道稳定 key。</summary>
    public required string Channel { get; init; }

    /// <summary>发布目标的显示名快照。</summary>
    public required string DisplayName { get; init; }

    /// <summary>关联的构建结果状态；发布未进入构建阶段时为空。</summary>
    public BuildStatus? BuildStatus { get; init; }

    /// <summary>关联的构建 session id；用于和高级日志对账。</summary>
    public Guid? BuildSessionId { get; init; }

    /// <summary>关联的构建指纹。</summary>
    public string? BuildFingerprint { get; init; }

    /// <summary>本次发布的静态输出文件数量。</summary>
    public int ArtifactCount { get; init; }

    /// <summary>远端提交 SHA。</summary>
    public string? RemoteCommitSha { get; init; }

    /// <summary>远端结果 URL。</summary>
    public string? RemoteUrl { get; init; }

    /// <summary>失败原因摘要；不包含凭据。</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>单个发布目标 publisher 的请求快照。</summary>
public sealed record PublishTargetRequest
{
    /// <summary>发布方案快照。</summary>
    public required PublishPlanRecord Plan { get; init; }

    /// <summary>非敏感配置 JSON。</summary>
    public required string ConfigurationJson { get; init; }

    /// <summary>解密后的凭据 JSON；只在调用 publisher 时短暂存在。</summary>
    public string? CredentialJson { get; init; }

    /// <summary>本地静态输出快照。</summary>
    public required StaticOutputSnapshot Output { get; init; }

    /// <summary>触发本次发布的构建结果。</summary>
    public required BuildResult BuildResult { get; init; }
}

/// <summary>单个发布目标 publisher 的成功返回值。</summary>
public sealed record PublishTargetResult
{
    /// <summary>远端提交 SHA。</summary>
    public string? RemoteCommitSha { get; init; }

    /// <summary>远端结果 URL。</summary>
    public string? RemoteUrl { get; init; }
}

/// <summary>发布目标失败；可携带已经成功生成的远端提交信息。</summary>
public sealed class PublishTargetException : Exception
{
    /// <summary>创建发布目标异常。</summary>
    public PublishTargetException(string message, string? remoteCommitSha = null, string? remoteUrl = null, Exception? innerException = null)
        : base(message, innerException)
    {
        RemoteCommitSha = remoteCommitSha;
        RemoteUrl = remoteUrl;
    }

    /// <summary>失败前已经产生的远端提交 SHA。</summary>
    public string? RemoteCommitSha { get; }

    /// <summary>失败前已经确认的远端 URL。</summary>
    public string? RemoteUrl { get; }
}
