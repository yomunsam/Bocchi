namespace Bocchi.Generator.Pipeline;

/// <summary>本次构建的可调参数。</summary>
public sealed record BuildOptions
{
    /// <summary>构建模式。</summary>
    public BuildMode Mode { get; init; } = BuildMode.FullBuild;

    /// <summary>构建环境（development / production / ...）。</summary>
    public string Environment { get; init; } = "production";

    /// <summary>是否纳入草稿。</summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>Feed 最多项数。<c>null</c> 表示沿用 <c>SiteSettings.FeedItemCount</c>。</summary>
    public int? FeedItemCount { get; init; }

    /// <summary>扫描错误是否阻塞构建。</summary>
    public bool FailOnContentError { get; init; } = true;

    /// <summary>在 Live 模式下：仅吐这一个 artifact（如 <c>posts.json</c>）；为空则吐全部。</summary>
    public string? OnlyArtifactPath { get; init; }

    /// <summary>实时模式专用：是否禁用"up-to-date 短路"（默认 Live 下禁用、Full 下启用）。</summary>
    public bool DisableUpToDateShortCircuit { get; init; }
}