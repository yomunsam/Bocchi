namespace Bocchi.ContentModel;

/// <summary>作品的外部链接条目（GitHub、官网、文档等）。</summary>
public sealed record WorkLink(string Label, string Url);

/// <summary>作品。对应 <c>Docs/Architecture.md §4.3</c>。</summary>
public sealed record Work
{
    public required string Slug { get; init; }

    public required string Title { get; init; }

    /// <summary>当前作品 variant 的语言代码；未显式声明时由扫描阶段填入 Site primary language。</summary>
    public string? Language { get; init; }

    /// <summary>当前作品的 localization group 与翻译来源信息。</summary>
    public ContentLocalization? Localization { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Draft;

    public string? Role { get; init; }

    public string? Period { get; init; }

    public MediaReference? Cover { get; init; }

    public IReadOnlyList<WorkLink> Links { get; init; } = [];

    public IReadOnlyList<string> Stack { get; init; } = [];

    public string? Summary { get; init; }

    public bool Featured { get; init; }
}
