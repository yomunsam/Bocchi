using Bocchi.ContentModel;

namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 文章在 <see cref="ContentGraph"/> 中的视图。包含已站点化的字段。
/// </summary>
public sealed record GraphPost
{
    /// <summary>内容 variant id，形态为 <c>{localizationGroup}@{language}</c>。</summary>
    public required string ContentId { get; init; }

    public required string Slug { get; init; }

    public required string Year { get; init; }

    public required string Title { get; init; }

    /// <summary>当前文章 variant 的有效语言。</summary>
    public required string Language { get; init; }

    /// <summary>当前文章的 localization group、翻译来源和同组 alternates。</summary>
    public required GraphContentLocalization Localization { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Published;

    public DateTimeOffset? PublishedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public string? Category { get; init; }

    /// <summary>匹配到的 Category 稳定 slug；为空表示文章没有 Category。</summary>
    public string? CategorySlug { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public string? Summary { get; init; }

    /// <summary>封面媒体（站点路径）。</summary>
    public MediaReference? Cover { get; init; }

    /// <summary>站点 URL（站点根相对，以 <c>/</c> 开头、以 <c>/</c> 结尾）。</summary>
    public required string SiteRelativeUrl { get; init; }

    /// <summary>原始 Markdown 正文（媒体引用已被改写为站点路径）。</summary>
    public required string BodyMarkdown { get; init; }

    /// <summary>由 Markdig 渲染得到的 HTML 正文（媒体引用已被改写为站点路径）。</summary>
    public required string BodyHtml { get; init; }

    /// <summary>正文摘要（已去 Markdown 标记）。</summary>
    public string? Excerpt { get; init; }

    /// <summary>本文引用的所有媒体资源（在 <see cref="ContentGraph.MediaAssets"/> 中可定位）。</summary>
    public IReadOnlyList<MediaReference> Media { get; init; } = [];
}
