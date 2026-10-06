using Bocchi.ContentModel;

namespace Bocchi.HomeServer.Data.State;

/// <summary>内容 workspace 中被扫描过的源文件索引；只记录指纹，不复制正文。</summary>
public sealed class ContentFileRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>相对内容 workspace 根的路径（统一用 <c>/</c> 分隔），全局唯一。</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>该文件承载的内容类型。</summary>
    public ContentKind Kind { get; set; }

    /// <summary>文件内容 SHA-256（大写十六进制）。</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>文件最后修改时间（UTC）。</summary>
    public DateTimeOffset LastModifiedAt { get; set; }

    /// <summary>最近一次扫描看到该文件的时间（UTC）。</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>由该文件产生的内容摘要；删除文件记录时级联删除。</summary>
    public List<ContentItemRecord> Items { get; } = [];
}
