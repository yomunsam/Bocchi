namespace Bocchi.ContentModel;

/// <summary>友链。对应 <c>Docs/Architecture.md §4.5</c>。</summary>
public sealed record FriendLink
{
    public required string Name { get; init; }

    public required string Url { get; init; }

    public MediaReference? Avatar { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public ContentStatus Status { get; init; } = ContentStatus.Published;

    public int Order { get; init; }
}
