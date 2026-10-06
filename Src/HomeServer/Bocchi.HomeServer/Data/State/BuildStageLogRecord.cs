namespace Bocchi.HomeServer.Data.State;

/// <summary>构建阶段日志。</summary>
public sealed class BuildStageLogRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>所属构建运行外键。</summary>
    public long BuildRunId { get; set; }

    /// <summary>所属构建运行。</summary>
    public BuildRunRecord? BuildRun { get; set; }

    /// <summary>发生时间（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>阶段名。</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>日志级别。</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>日志内容。</summary>
    public string Message { get; set; } = string.Empty;
}
