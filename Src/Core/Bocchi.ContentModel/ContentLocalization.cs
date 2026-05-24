namespace Bocchi.ContentModel;

/// <summary>
/// 内容多语言分组信息。Post / Page / Work 使用它把同一份内容的不同语言版本关联起来。
/// </summary>
public sealed record ContentLocalization
{
    /// <summary>同一篇内容在不同语言下共享的逻辑分组 id，例如 <c>posts/2026/hello-bocchi</c>。</summary>
    public required string GroupId { get; init; }

    /// <summary>当前 variant 的翻译来源；为空表示它是 Native variant。</summary>
    public ContentTranslationSource? TranslationOf { get; init; }
}

/// <summary>Translation variant 记录的来源内容信息。</summary>
public sealed record ContentTranslationSource
{
    /// <summary>来源 variant 的语言代码。</summary>
    public required string Language { get; init; }

    /// <summary>来源内容 id；为空时由 <see cref="ContentLocalization.GroupId"/> 与 <see cref="Language"/> 推导。</summary>
    public string? ContentId { get; init; }
}
