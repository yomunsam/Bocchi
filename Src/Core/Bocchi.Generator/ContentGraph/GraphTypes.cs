using Bocchi.ContentModel;

namespace Bocchi.Generator.ContentGraph;

/// <summary>内容图中通用的多语言 variant 关系。</summary>
public sealed record GraphContentLocalization
{
    /// <summary>同一内容在不同语言下共享的逻辑分组 id。</summary>
    public required string GroupId { get; init; }

    /// <summary>当前 variant 是否声明自己翻译自另一个 variant。</summary>
    public required bool IsTranslation { get; init; }

    /// <summary>Translation variant 的来源语言。</summary>
    public string? SourceLanguage { get; init; }

    /// <summary>Translation variant 的来源内容 id。</summary>
    public string? SourceContentId { get; init; }

    /// <summary>同一 localization group 中实际进入当前内容图的语言版本。</summary>
    public IReadOnlyList<GraphContentAlternate> Alternates { get; init; } = [];
}

/// <summary>同一 localization group 中可切换的另一个内容版本。</summary>
public sealed record GraphContentAlternate
{
    /// <summary>目标 variant 的内容 id。</summary>
    public required string ContentId { get; init; }

    /// <summary>目标 variant 的语言代码。</summary>
    public required string Language { get; init; }

    /// <summary>目标 variant 的标题。</summary>
    public required string Title { get; init; }

    /// <summary>目标 variant 的站点根相对 URL。</summary>
    public required string Url { get; init; }
}

/// <summary>独立页面在 <see cref="ContentGraph"/> 中的视图。</summary>
public sealed record GraphPage
{
    /// <summary>内容 variant id，形态为 <c>{localizationGroup}@{language}</c>。</summary>
    public required string ContentId { get; init; }

    public required string Slug { get; init; }

    public required string Title { get; init; }

    /// <summary>当前页面 variant 的有效语言。</summary>
    public required string Language { get; init; }

    /// <summary>当前页面的 localization group、翻译来源和同组 alternates。</summary>
    public required GraphContentLocalization Localization { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Published;

    public int Order { get; init; }

    public bool ShowInNavigation { get; init; }

    public string? Summary { get; init; }

    /// <summary>Page 使用的 Theme 模板名称。</summary>
    public string Template { get; init; } = "normal";

    public required string SiteRelativeUrl { get; init; }

    public required string BodyMarkdown { get; init; }

    public required string BodyHtml { get; init; }

    public string? Excerpt { get; init; }

    public IReadOnlyList<MediaReference> Media { get; init; } = [];
}

/// <summary>构建入口注入的 Category tree 快照。Generator 不直接读取 HomeServer 数据库。</summary>
public sealed record BuildCategoryNode
{
    /// <summary>稳定节点 id，主要用于空 slug fallback。</summary>
    public required string Id { get; init; }

    /// <summary>类别显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>类别稳定 URL slug；为空时 Generator 会从名称或 id 归一化。</summary>
    public string? Slug { get; init; }

    /// <summary>下一层子类别。</summary>
    public IReadOnlyList<BuildCategoryNode> Children { get; init; } = [];

    /// <summary>多语言显示名覆盖（langCode → 显示名）；为空表示沿用 <see cref="Name"/>。</summary>
    public IReadOnlyDictionary<string, string>? LocalizedNames { get; init; }
}

/// <summary>内容图中的 Post Category 节点，已经带有稳定 URL 与文章数量。</summary>
public sealed record GraphPostCategory
{
    /// <summary>类别显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>稳定 URL slug。</summary>
    public required string Slug { get; init; }

    /// <summary>站点根相对 URL。</summary>
    public required string SiteRelativeUrl { get; init; }

    /// <summary>当前类别及其子类别直接匹配到的文章数量。</summary>
    public required int Count { get; init; }

    /// <summary>下一层类别。</summary>
    public IReadOnlyList<GraphPostCategory> Children { get; init; } = [];
}

/// <summary>作品视图。</summary>
public sealed record GraphWork
{
    /// <summary>内容 variant id，形态为 <c>{localizationGroup}@{language}</c>。</summary>
    public required string ContentId { get; init; }

    public required string Slug { get; init; }

    public required string Year { get; init; }

    public required string Title { get; init; }

    /// <summary>当前作品 variant 的有效语言。</summary>
    public required string Language { get; init; }

    /// <summary>当前作品的 localization group、翻译来源和同组 alternates。</summary>
    public required GraphContentLocalization Localization { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Published;

    public string? Role { get; init; }

    public string? Period { get; init; }

    public MediaReference? Cover { get; init; }

    public IReadOnlyList<WorkLink> Links { get; init; } = [];

    public IReadOnlyList<string> Stack { get; init; } = [];

    public string? Summary { get; init; }

    public bool Featured { get; init; }

    public required string SiteRelativeUrl { get; init; }

    public required string BodyMarkdown { get; init; }

    public required string BodyHtml { get; init; }

    public string? Excerpt { get; init; }

    public IReadOnlyList<MediaReference> Media { get; init; } = [];
}

/// <summary>短文视图。短文 <c>Text</c> 与 <c>BodyMarkdown</c> 是同一份事实。</summary>
public sealed record GraphNote
{
    public required string Id { get; init; }

    public required string Year { get; init; }

    /// <summary>短文详情页的站点根相对 URL，使用公开稳定 id 而不是 workspace 日期目录。</summary>
    public required string SiteRelativeUrl { get; init; }

    public ContentStatus Status { get; init; } = ContentStatus.Published;

    public DateTimeOffset? PublishedAt { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public required string BodyMarkdown { get; init; }

    public required string BodyHtml { get; init; }

    public string? Excerpt { get; init; }

    public IReadOnlyList<MediaReference> Media { get; init; } = [];
}

/// <summary>友链视图。</summary>
public sealed record GraphFriend
{
    public required string Name { get; init; }

    public required string Url { get; init; }

    public MediaReference? Avatar { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public ContentStatus Status { get; init; } = ContentStatus.Published;

    public int Order { get; init; }
}
