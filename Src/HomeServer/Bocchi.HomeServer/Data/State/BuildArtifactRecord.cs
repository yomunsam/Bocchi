namespace Bocchi.HomeServer.Data.State;

/// <summary>构建产物登记。</summary>
public sealed class BuildArtifactRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>所属构建运行外键。</summary>
    public long BuildRunId { get; set; }

    /// <summary>所属构建运行。</summary>
    public BuildRunRecord? BuildRun { get; set; }

    /// <summary>站点根相对路径。</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>产物类型。</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>MIME 类型。</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>字节数。</summary>
    public long SizeBytes { get; set; }

    /// <summary>内容 SHA-256。</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>产生该产物的阶段。</summary>
    public string ProducedBy { get; set; } = string.Empty;
}
