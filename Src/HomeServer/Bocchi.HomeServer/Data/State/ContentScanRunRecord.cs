namespace Bocchi.HomeServer.Data.State;

/// <summary>一次内容扫描运行。</summary>
public sealed class ContentScanRunRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>开始时间（UTC）。</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>结束时间（UTC），运行中为空。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>扫描的文件数。</summary>
    public int FilesScanned { get; set; }

    /// <summary>成功加载的内容项数。</summary>
    public int ItemsLoaded { get; set; }

    /// <summary>错误条数。</summary>
    public int ErrorCount { get; set; }

    /// <summary>警告条数。</summary>
    public int WarningCount { get; set; }

    /// <summary>扫描时内容 workspace 的 Git HEAD。</summary>
    public string? GitHeadSha { get; set; }

    /// <summary>运行状态：<c>running</c> / <c>succeeded</c> / <c>failed</c> / <c>cancelled</c>。</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>本次扫描产生的诊断；删除扫描记录时级联删除。</summary>
    public List<ContentErrorRecord> Errors { get; } = [];
}
