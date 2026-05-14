using Bocchi.ContentModel;

namespace Bocchi.Generator.ThemeInputs.Models;

/// <summary>对应 <c>.bocchi/input/site.json</c> 的 <c>data</c> 主体。</summary>
public sealed record SiteInput
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Language { get; init; }
    public required string TimeZone { get; init; }
    public required string BaseUrl { get; init; }
    public AuthorInfo? Author { get; init; }
    public required IReadOnlyList<SocialLink> Social { get; init; }
    public required bool EnableRss { get; init; }
    public required bool EnableSitemap { get; init; }
    public required bool EnableSearch { get; init; }
    public required int FeedItemCount { get; init; }
}

/// <summary>对应 <c>.bocchi/input/navigation.json</c>。</summary>
public sealed record NavigationInput
{
    public required IReadOnlyList<NavigationItem> Items { get; init; }
}

/// <summary>Theme 输入数据中通用的 Post 表达。包含三态正文。</summary>
public sealed record PostInput
{
    public required string Slug { get; init; }
    public required string Year { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? Category { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public string? Summary { get; init; }
    public MediaReferenceInput? Cover { get; init; }
    public required string Url { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public string? Excerpt { get; init; }
    public required IReadOnlyList<MediaReferenceInput> Media { get; init; }
}

/// <summary>独立页面。</summary>
public sealed record PageInput
{
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public required int Order { get; init; }
    public required bool ShowInNavigation { get; init; }
    public string? Summary { get; init; }
    public required string Url { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public string? Excerpt { get; init; }
    public required IReadOnlyList<MediaReferenceInput> Media { get; init; }
}

/// <summary>作品。</summary>
public sealed record WorkInput
{
    public required string Slug { get; init; }
    public required string Year { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public string? Role { get; init; }
    public string? Period { get; init; }
    public MediaReferenceInput? Cover { get; init; }
    public required IReadOnlyList<WorkLink> Links { get; init; }
    public required IReadOnlyList<string> Stack { get; init; }
    public string? Summary { get; init; }
    public required bool Featured { get; init; }
    public required string Url { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public string? Excerpt { get; init; }
    public required IReadOnlyList<MediaReferenceInput> Media { get; init; }
}

/// <summary>短文。</summary>
public sealed record NoteInput
{
    public required string Id { get; init; }
    public required string Year { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public string? Excerpt { get; init; }
    public required IReadOnlyList<MediaReferenceInput> Media { get; init; }
}

/// <summary>友链。</summary>
public sealed record FriendInput
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public MediaReferenceInput? Avatar { get; init; }
    public string? Description { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string Status { get; init; }
    public required int Order { get; init; }
}

/// <summary>媒体引用。<see cref="Path"/> 总是站点根相对（以 <c>/</c> 开头）。</summary>
public sealed record MediaReferenceInput(string Path, string? Alt = null);
