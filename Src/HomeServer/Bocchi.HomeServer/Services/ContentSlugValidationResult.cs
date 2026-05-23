namespace Bocchi.HomeServer.Services;

/// <summary>Home Server 对内容路径标识做规范化与可用性检查后的结果。</summary>
public sealed class ContentSlugValidationResult
{
    /// <summary>构造一个路径标识检查结果。</summary>
    public ContentSlugValidationResult(bool isAvailable, string slug, string? reason)
    {
        IsAvailable = isAvailable;
        Slug = slug;
        Reason = reason;
    }

    /// <summary>规范化后的 slug；不可用时仍会回传规范化值，便于 AI 重试时告知模型。</summary>
    public string Slug { get; }

    /// <summary>当前 slug 是否可以用于目标内容的最终 URL。</summary>
    public bool IsAvailable { get; }

    /// <summary>不可用原因；可用时为空。</summary>
    public string? Reason { get; }

    /// <summary>创建一个可用结果。</summary>
    public static ContentSlugValidationResult Available(string slug)
        => new(true, slug, null);

    /// <summary>创建一个不可用结果。</summary>
    public static ContentSlugValidationResult Unavailable(string slug, string reason)
        => new(false, slug, reason);
}
