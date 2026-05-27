using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Bocchi.ContentModel;

namespace Bocchi.Generator.ThemeInputs.Models;

/// <summary>对应 <c>../../cache/theme-input/site.json</c> 的 <c>data</c> 主体。</summary>
public sealed record SiteInput
{
    public required string Title { get; init; }
    public string? DefaultTitle { get; init; }
    public string? Description { get; init; }
    public required string Language { get; init; }
    public required string TimeZone { get; init; }
    public required string BaseUrl { get; init; }
    public string? CopyrightNotice { get; init; }
    public AuthorInfo? Author { get; init; }
    public required IReadOnlyList<SocialLink> Social { get; init; }
    public required bool EnableRss { get; init; }
    public required bool EnableSitemap { get; init; }
    public required bool EnableSearch { get; init; }
    public required int FeedItemCount { get; init; }
}

/// <summary>对应 <c>../../cache/theme-input/navigation.json</c>。</summary>
public sealed record NavigationInput
{
    public required IReadOnlyList<NavigationItemInput> Items { get; init; }
}

/// <summary>Theme 输入中的 Menu tree 节点；分组节点可以没有 URL。</summary>
public sealed record NavigationItemInput
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public NavigationLabelI18nRefInput? LabelI18n { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? Href { get; init; }
    public NavigationTargetInput? Target { get; init; }
    public required IReadOnlyList<NavigationItemInput> Children { get; init; }
}

/// <summary>Menu label 使用的 i18n 引用，Theme 可据此在浏览器端切换文案。</summary>
public sealed record NavigationLabelI18nRefInput
{
    public required string Scope { get; init; }
    public required string Key { get; init; }
    public required string Raw { get; init; }
}

/// <summary>Theme 输入中保留的原始 Menu target。</summary>
public sealed record NavigationTargetInput
{
    public required string Type { get; init; }
    public string? Value { get; init; }
}

/// <summary>Theme 输入数据中通用的 Post 表达。包含三态正文。</summary>
public sealed record PostInput
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Year { get; init; }
    public required string Title { get; init; }
    public required string Language { get; init; }
    public required ContentLocalizationInput Localization { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? Category { get; init; }
    public string? CategorySlug { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public string? Summary { get; init; }
    public MediaReferenceInput? Cover { get; init; }
    /// <summary>站点根相对 URL。新 Theme 应优先使用它；<see cref="Url"/> 保留为 v1 兼容别名。</summary>
    public required string SiteRelativeUrl { get; init; }
    /// <summary>当前语言页面自身的绝对 canonical URL，Theme 不需要再从 baseUrl 推导。</summary>
    public required string CanonicalUrl { get; init; }
    /// <summary>站点根相对 URL 的兼容别名。</summary>
    public required string Url { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public string? Excerpt { get; init; }
    public required IReadOnlyList<MediaReferenceInput> Media { get; init; }
}

/// <summary>独立页面。</summary>
public sealed record PageInput
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required string Language { get; init; }
    public required ContentLocalizationInput Localization { get; init; }
    public required string Status { get; init; }
    public required int Order { get; init; }
    public required bool ShowInNavigation { get; init; }
    public string? Summary { get; init; }
    public required string Template { get; init; }
    /// <summary>站点根相对 URL。新 Theme 应优先使用它；<see cref="Url"/> 保留为 v1 兼容别名。</summary>
    public required string SiteRelativeUrl { get; init; }
    /// <summary>当前语言页面自身的绝对 canonical URL，Theme 不需要再从 baseUrl 推导。</summary>
    public required string CanonicalUrl { get; init; }
    /// <summary>站点根相对 URL 的兼容别名。</summary>
    public required string Url { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public string? Excerpt { get; init; }
    public required IReadOnlyList<MediaReferenceInput> Media { get; init; }
}

/// <summary>作品。</summary>
public sealed record WorkInput
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Year { get; init; }
    public required string Title { get; init; }
    public required string Language { get; init; }
    public required ContentLocalizationInput Localization { get; init; }
    public required string Status { get; init; }
    public string? Role { get; init; }
    public string? Period { get; init; }
    public MediaReferenceInput? Cover { get; init; }
    public required IReadOnlyList<WorkLink> Links { get; init; }
    public required IReadOnlyList<string> Stack { get; init; }
    public string? Summary { get; init; }
    public required bool Featured { get; init; }
    /// <summary>站点根相对 URL。新 Theme 应优先使用它；<see cref="Url"/> 保留为 v1 兼容别名。</summary>
    public required string SiteRelativeUrl { get; init; }
    /// <summary>当前语言页面自身的绝对 canonical URL，Theme 不需要再从 baseUrl 推导。</summary>
    public required string CanonicalUrl { get; init; }
    /// <summary>站点根相对 URL 的兼容别名。</summary>
    public required string Url { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public string? Excerpt { get; init; }
    public required IReadOnlyList<MediaReferenceInput> Media { get; init; }
}

/// <summary>Theme 输入中通用的内容多语言关系。</summary>
public sealed record ContentLocalizationInput
{
    public required string GroupId { get; init; }
    public required bool IsTranslation { get; init; }
    public string? SourceLanguage { get; init; }
    public string? SourceContentId { get; init; }
    public required IReadOnlyList<ContentAlternateInput> Alternates { get; init; }
}

/// <summary>Theme 输入中同一 localization group 的一个可切换语言版本。</summary>
public sealed record ContentAlternateInput
{
    public required string ContentId { get; init; }
    public required string Language { get; init; }
    /// <summary>SEO link 使用的 hreflang 值；当前等于语言代码，后续可扩展 x-default。</summary>
    public required string Hreflang { get; init; }
    public required string Title { get; init; }
    /// <summary>目标 variant 的站点根相对 URL。</summary>
    public required string SiteRelativeUrl { get; init; }
    /// <summary>目标 variant 的站点根相对 URL 兼容别名。</summary>
    public required string Url { get; init; }
    /// <summary>目标 variant 的绝对 URL，用于 <c>link rel="alternate"</c>。</summary>
    public required string Href { get; init; }
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

/// <summary>对应 <c>../../cache/theme-input/post-categories.json</c> 的 Post Category tree 节点。</summary>
public sealed record PostCategoryInput
{
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string SiteRelativeUrl { get; init; }
    public required string Url { get; init; }
    public required int Count { get; init; }
    public required IReadOnlyList<PostCategoryInput> Children { get; init; }
}

/// <summary>媒体引用。<see cref="Path"/> 总是站点根相对（以 <c>/</c> 开头）。</summary>
public sealed record MediaReferenceInput(string Path, string? Alt = null);

/// <summary>对应 <c>../../cache/theme-input/theme-context.json</c> 的全局渲染上下文。</summary>
public sealed record ThemeContextInput
{
    public required ThemeContextBocchi Bocchi { get; init; }
    public required ThemeContextBuild Build { get; init; }
    public required ThemeContextSite Site { get; init; }
    /// <summary>前台 Theme 使用的站点本地化上下文；Dashboard UI language 不进入这里。</summary>
    public required ThemeContextLocalization Localization { get; init; }
    public required ThemeContextAuthor Author { get; init; }
    public required ThemeContextFeatures Features { get; init; }
    public required ThemeContextTheme Theme { get; init; }
}

/// <summary>Bocchi 程序自身信息。</summary>
public sealed record ThemeContextBocchi
{
    public string? Version { get; init; }
}

/// <summary>本次构建的运行信息。</summary>
public sealed record ThemeContextBuild
{
    /// <summary>构建模式。<c>full</c> 表示写入发布输出，<c>live</c> 表示 Home Server 实时预览。</summary>
    public required string Mode { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
    public required string Environment { get; init; }
    public required bool IncludeDrafts { get; init; }
}

/// <summary>Theme 渲染所需的站点事实。</summary>
public sealed record ThemeContextSite
{
    public required string Title { get; init; }
    public string? DefaultTitle { get; init; }
    public string? Description { get; init; }
    public required string Language { get; init; }
    public required string TimeZone { get; init; }
    public required string BaseUrl { get; init; }
    public string? CopyrightNotice { get; init; }
}

/// <summary>Theme Contract 的站点本地化节点，表达站点语言、URL policy 与 Common i18n 文本覆盖。</summary>
public sealed record ThemeContextLocalization
{
    /// <summary>站点主要语言；PrimaryUnprefixed 策略下该语言使用无前缀 URL。</summary>
    public required string PrimaryLanguage { get; init; }

    /// <summary>站点启用语言列表，调用方会保证包含主要语言。</summary>
    public required IReadOnlyList<ThemeContextLanguageRecord> EnabledLanguages { get; init; }

    /// <summary>M6 固定 URL policy：主语言无前缀，其他启用语言使用语言前缀。</summary>
    public required string UrlPolicy { get; init; }

    /// <summary>Common i18n key 覆盖；没有用户覆盖时保持为空对象。</summary>
    public JsonObject Text { get; init; } = new();
}

/// <summary>传给 Theme 的语言描述，不包含图标或地区视觉符号。</summary>
public sealed record ThemeContextLanguageRecord
{
    /// <summary>BCP 47 风格语言代码，例如 <c>en-US</c> 或 <c>zh-CN</c>。</summary>
    public required string Code { get; init; }

    /// <summary>该语言自己的显示名称。</summary>
    public required string NativeName { get; init; }

    /// <summary>该语言的英文显示名称，便于 Theme 做跨语言 fallback 展示。</summary>
    public required string EnglishName { get; init; }
}

/// <summary>Theme 渲染所需的作者信息。</summary>
public sealed record ThemeContextAuthor
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string TimeZone { get; init; }
    public string? Email { get; init; }
    public string? Bio { get; init; }
    public required IReadOnlyList<SocialLink> Links { get; init; }
}

/// <summary>跨 Theme 的站点功能开关。</summary>
public sealed record ThemeContextFeatures
{
    public required bool Rss { get; init; }
    public required bool Sitemap { get; init; }
    public required bool Search { get; init; }
}

/// <summary>当前 Theme 的身份与有效配置。</summary>
public sealed record ThemeContextTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public JsonObject Config { get; init; } = new();
    /// <summary>当前 Theme manifest 声明的私有 i18n key；Theme 未声明时保留空列表。</summary>
    public ThemeContextThemeI18n I18n { get; init; } = new();
    /// <summary>当前 Theme 接受的 Page 模板声明；至少包含 normal。</summary>
    public IReadOnlyList<ThemeContextPageTemplate> PageTemplates { get; init; } = [];
    /// <summary>当前 Theme 自己提供的特殊页面声明。</summary>
    public IReadOnlyList<ThemeContextSpecialPage> SpecialPages { get; init; } = [];
}

/// <summary>Theme Context 中的 Theme 私有 i18n 声明快照。</summary>
public sealed record ThemeContextThemeI18n
{
    /// <summary>Theme 原生支持或提供默认值的语言代码列表。</summary>
    public IReadOnlyList<string> SupportedLanguages { get; init; } = [];

    /// <summary>Theme 默认语言；为空时由 Theme 自行决定 fallback。</summary>
    public string? DefaultLanguage { get; init; }

    /// <summary>Theme 私有 key 列表。Common key 不会重复出现在这里。</summary>
    public IReadOnlyList<ThemeContextThemeI18nKey> Keys { get; init; } = [];
}

/// <summary>Theme Context 中的单个 Theme 私有 i18n key 声明。</summary>
public sealed record ThemeContextThemeI18nKey
{
    /// <summary>Theme 私有 key，通常带 Theme 命名空间。</summary>
    public required string Key { get; init; }

    /// <summary>Dashboard 可展示的短标题。</summary>
    public required string Title { get; init; }

    /// <summary>Dashboard 可展示的说明文本。</summary>
    public string? Description { get; init; }

    /// <summary>Theme manifest 提供的默认 plain text 值。</summary>
    public JsonObject DefaultValues { get; init; } = new();
}

/// <summary>Theme Context 中的 Page 模板声明。</summary>
public sealed record ThemeContextPageTemplate
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>Theme Context 中的特殊页面声明。</summary>
public sealed record ThemeContextSpecialPage
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Route { get; init; }
}
