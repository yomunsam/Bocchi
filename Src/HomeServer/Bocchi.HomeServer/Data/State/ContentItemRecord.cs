using Bocchi.ContentModel;

namespace Bocchi.HomeServer.Data.State;

/// <summary>内容摘要索引，供后台列表、导航和预览路由使用；(Kind, ContentId) 唯一。</summary>
public sealed class ContentItemRecord
{
    /// <summary>数据库主键。</summary>
    public long Id { get; set; }

    /// <summary>来源文件外键。</summary>
    public long FileId { get; set; }

    /// <summary>来源文件。</summary>
    public ContentFileRecord? File { get; set; }

    /// <summary>内容类型。</summary>
    public ContentKind Kind { get; set; }

    /// <summary>业务 id（slug、Note id、友链 URL 等）。</summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>URL slug，可空。</summary>
    public string? Slug { get; set; }

    /// <summary>标题，可空。</summary>
    public string? Title { get; set; }

    /// <summary>发布状态。</summary>
    public ContentStatus Status { get; set; }

    /// <summary>年份目录（Post / Work / Note），可空。</summary>
    public string? Year { get; set; }

    /// <summary>发布时间，可空。</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>更新时间，可空。</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>frontmatter 快照 JSON，可空。</summary>
    public string? FrontmatterJson { get; set; }

    /// <summary>内容 variant 的有效语言。</summary>
    public string? Language { get; set; }

    /// <summary>localization group id。</summary>
    public string? LocalizationGroup { get; set; }

    /// <summary>是否为 Translation variant。</summary>
    public bool IsTranslation { get; set; }

    /// <summary>Translation variant 的来源语言。</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>Translation variant 的来源内容 id。</summary>
    public string? SourceContentId { get; set; }

    /// <summary>最近一次扫描看到该条目的时间（UTC）。</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
