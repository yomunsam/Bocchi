namespace Bocchi.ContentModel;

/// <summary>独立页面，例如 About。对应 <c>Docs/Architecture.md §4.2</c>。</summary>
public sealed record Page
{
    public required string Slug { get; init; }

    public required string Title { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Draft;

    public int Order { get; init; }

    public bool ShowInNavigation { get; init; }

    public string? Summary { get; init; }
}