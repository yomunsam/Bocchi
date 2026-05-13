namespace Bocchi.ContentModel;

/// <summary>照片墙条目。预留类型，对应 <c>Docs/Architecture.md §4.7</c>。</summary>
public sealed record Photo
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    public DateTimeOffset? TakenAt { get; init; }

    public string? Location { get; init; }

    public required MediaReference Media { get; init; }

    public IReadOnlyList<string> Albums { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? Description { get; init; }
}
