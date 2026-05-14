namespace Bocchi.Generator.Pipeline;

/// <summary>
/// 单次构建产物。<see cref="Path"/> 是站点根相对路径（以 <c>/</c> 开头）；
/// 不同 <see cref="ArtifactKind"/> 的产物落到不同输出根：见 <c>Docs/Milestones/M3/M3.md §3.7</c>。
/// </summary>
public sealed record BuildArtifact
{
    /// <summary>站点根相对路径（以 <c>/</c> 开头、<c>/</c> 归一化）。</summary>
    public required string Path { get; init; }

    /// <summary>归属：决定 Sink 落地到 <c>output/public/</c> 还是 <c>.bocchi/input/</c> 等。</summary>
    public required ArtifactKind Kind { get; init; }

    /// <summary>MIME 类型。</summary>
    public required string ContentType { get; init; }

    /// <summary>产物字节大小（已知时填）。</summary>
    public required long SizeBytes { get; init; }

    /// <summary>SHA-256（小写十六进制）。</summary>
    public required string Sha256 { get; init; }

    /// <summary>产物来源阶段。</summary>
    public required string ProducedBy { get; init; }

    /// <summary>本地源文件（用于媒体复制）；为空时表示产物在内存中。</summary>
    public string? SourceAbsolutePath { get; init; }

    /// <summary>内存中的字节内容；为空时表示来源于 <see cref="SourceAbsolutePath"/>。</summary>
    public ReadOnlyMemory<byte>? Bytes { get; init; }
}

/// <summary>Artifact 类别 → 输出根。</summary>
public enum ArtifactKind
{
    /// <summary>Theme 输入数据 JSON，落到 <c>.bocchi/input/</c>。</summary>
    ThemeInput,

    /// <summary>站点级功能性产物（robots / sitemap / feed / manifest），落到 <c>output/public/</c>。</summary>
    SiteArtifact,

    /// <summary>媒体复制，落到 <c>output/public/media/...</c>。</summary>
    Media,

    /// <summary>Theme 渲染输出（HTML / 静态资源），落到 <c>output/public/</c>。M5 落地。</summary>
    ThemeOutput,
}