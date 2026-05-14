namespace Bocchi.ContentModel;

/// <summary>短文。类似 Twitter 的轻量内容流，对应 <c>Docs/Architecture.md §4.4</c>。</summary>
public sealed record Note
{
    public required string Id { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Draft;

    public DateTimeOffset? PublishedAt { get; init; }

    public required string Text { get; init; }

    public IReadOnlyList<MediaReference> Media { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];
}