namespace Bocchi.ContentModel;

/// <summary>
/// 文章。基于 Markdown 文件，参与时间线、Feed、Sitemap。字段对应 <c>Docs/Architecture.md §4.1</c>。
/// </summary>
/// <remarks>
/// M1 仅落地类型契约。M2 阶段会在扫描 Markdown 时把 frontmatter 解析为本类型实例。
/// </remarks>
public sealed record Post
{
    public required string Slug { get; init; }

    public required string Title { get; init; }

    /// <summary>当前文章 variant 的语言代码；未显式声明时由扫描阶段填入 Site primary language。</summary>
    public string? Language { get; init; }

    /// <summary>当前文章的 localization group 与翻译来源信息。</summary>
    public ContentLocalization? Localization { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Draft;

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? Summary { get; init; }

    public MediaReference? Cover { get; init; }
}
