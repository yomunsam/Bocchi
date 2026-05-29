namespace Bocchi.ContentModel;

/// <summary>短文。使用 8 位短 id 作为公开稳定标识，对应 <c>Docs/Architecture.md §4.4</c>。</summary>
public sealed record Note
{
    /// <summary>8 位小写字母数字短 id，公开 URL 为 <c>/notes/{id}/</c>。</summary>
    public required string Id { get; init; }

    /// <summary>发布状态，默认草稿。</summary>
    public ContentStatus Status { get; init; } = ContentStatus.Draft;

    /// <summary>短文发布时间；缺失时仍保留内容，但排序只能使用兜底值。</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>短文 Markdown 正文。</summary>
    public required string Text { get; init; }

    /// <summary>frontmatter 或正文中声明的媒体引用。</summary>
    public IReadOnlyList<MediaReference> Media { get; init; } = [];

    /// <summary>短文标签。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
