using Bocchi.Generator.Pipeline;

namespace Bocchi.HomeServer.Data.State;

/// <summary>一次持久化的站点构建运行（Live 预览不落库）。</summary>
public sealed class BuildRunRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>构建会话 id，唯一。</summary>
    public Guid SessionId { get; set; }

    /// <summary>本次构建使用的扫描运行外键，扫描记录被清理时置空。</summary>
    public long? ScanRunId { get; set; }

    /// <summary>本次构建使用的扫描运行。</summary>
    public ContentScanRunRecord? ScanRun { get; set; }

    /// <summary>构建模式。</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>构建环境名。</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>使用的 Theme id。</summary>
    public string? ThemeId { get; set; }

    /// <summary>是否包含草稿。</summary>
    public bool IncludeDrafts { get; set; }

    /// <summary>开始时间（UTC）。</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>结束时间（UTC），运行中为空。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>最终状态；运行中为空。</summary>
    public BuildStatus? Status { get; set; }

    /// <summary>内容 + Theme + 选项指纹。</summary>
    public string? Fingerprint { get; set; }

    /// <summary>失败或跳过原因。</summary>
    public string? Reason { get; set; }

    /// <summary>执行构建的 Bocchi 版本。</summary>
    public string? BocchiVersion { get; set; }

    /// <summary>本次构建登记的产物；删除构建记录时级联删除。</summary>
    public List<BuildArtifactRecord> Artifacts { get; } = [];

    /// <summary>本次构建的阶段日志；删除构建记录时级联删除。</summary>
    public List<BuildStageLogRecord> Logs { get; } = [];
}
