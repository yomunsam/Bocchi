namespace Bocchi.ContentModel;

/// <summary>独立页面，例如 About。对应 <c>Docs/Architecture.md §4.2</c>。</summary>
public sealed record Page
{
    /// <summary>页面 slug，决定独立页面 URL。</summary>
    public required string Slug { get; init; }

    /// <summary>页面标题。</summary>
    public required string Title { get; init; }

    /// <summary>当前页面 variant 的语言代码；未显式声明时由扫描阶段填入 Site primary language。</summary>
    public string? Language { get; init; }

    /// <summary>当前页面的 localization group 与翻译来源信息。</summary>
    public ContentLocalization? Localization { get; init; }

    /// <summary>发布状态。Generator 默认只输出 published 页面，预览可包含 draft。</summary>
    public ContentStatus Status { get; init; } = ContentStatus.Draft;

    /// <summary>列表排序值，数值小的页面排在前面。</summary>
    public int Order { get; init; }

    /// <summary>历史字段；正式前台 Menu v1 不再依赖它自动生成导航。</summary>
    public bool ShowInNavigation { get; init; }

    /// <summary>页面摘要。</summary>
    public string? Summary { get; init; }

    /// <summary>Theme 页面模板名称。只保存 name，不保存 Theme id 或关联记录。</summary>
    public string Template { get; init; } = "normal";
}
