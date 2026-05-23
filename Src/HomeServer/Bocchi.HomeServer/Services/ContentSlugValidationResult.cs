namespace Bocchi.HomeServer.Services;

/// <summary>Home Server 对内容路径标识做规范化与可用性检查后的结果。</summary>
public sealed class ContentSlugValidationResult
{
    /// <summary>构造一个路径标识检查结果。</summary>
    public ContentSlugValidationResult(
        bool isAvailable,
        string slug,
        string? reason,
        ContentSlugValidationIssue issue = ContentSlugValidationIssue.None)
    {
        IsAvailable = isAvailable;
        Slug = slug;
        Reason = reason;
        Issue = issue;
    }

    /// <summary>规范化后的 slug；不可用时仍会回传规范化值，便于 AI 重试时告知模型。</summary>
    public string Slug { get; }

    /// <summary>当前 slug 是否可以用于目标内容的最终 URL。</summary>
    public bool IsAvailable { get; }

    /// <summary>不可用原因；可用时为空。</summary>
    public string? Reason { get; }

    /// <summary>不可用原因的结构化代码，供 Dashboard 按当前 UI 语言渲染文案。</summary>
    public ContentSlugValidationIssue Issue { get; }

    /// <summary>创建一个可用结果。</summary>
    public static ContentSlugValidationResult Available(string slug)
        => new(true, slug, null);

    /// <summary>创建一个不可用结果。</summary>
    public static ContentSlugValidationResult Unavailable(
        string slug,
        string reason,
        ContentSlugValidationIssue issue = ContentSlugValidationIssue.None)
        => new(false, slug, reason, issue);
}

/// <summary>内容路径标识不可用的结构化原因。</summary>
public enum ContentSlugValidationIssue
{
    /// <summary>没有结构化原因；调用方可使用通用 fallback。</summary>
    None,

    /// <summary>候选 slug 为空或规范化后为空。</summary>
    Empty,

    /// <summary>Page slug 与系统保留路由冲突。</summary>
    ReservedRoute,

    /// <summary>Page slug 已被其它页面使用。</summary>
    PageTaken,

    /// <summary>当前内容路径无法判断年份，不能做年内唯一性检查。</summary>
    YearUnavailable,

    /// <summary>Post 或 Work slug 已在同一年份内被其它内容使用。</summary>
    YearScopedTaken,
}
