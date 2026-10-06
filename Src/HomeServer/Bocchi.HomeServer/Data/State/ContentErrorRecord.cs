using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

namespace Bocchi.HomeServer.Data.State;

/// <summary>扫描运行产生的一条内容诊断（错误 / 警告 / 提示）。</summary>
public sealed class ContentErrorRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>所属扫描运行外键。</summary>
    public long ScanRunId { get; set; }

    /// <summary>所属扫描运行。</summary>
    public ContentScanRunRecord? ScanRun { get; set; }

    /// <summary>出错文件相对路径。</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>内容类型，可空。</summary>
    public ContentKind? Kind { get; set; }

    /// <summary>出错字段，可空。</summary>
    public string? Field { get; set; }

    /// <summary>严重级别。</summary>
    public ContentErrorSeverity Severity { get; set; }

    /// <summary>稳定诊断 code。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>诊断说明。</summary>
    public string Message { get; set; } = string.Empty;
}
