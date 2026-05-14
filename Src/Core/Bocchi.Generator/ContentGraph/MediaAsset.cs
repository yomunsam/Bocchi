using Bocchi.ContentModel;

namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 站点视角的媒体资源条目。
/// </summary>
/// <remarks>
/// 一个媒体资源由"绝对源路径"唯一标识；不同 Owner 引用同一个文件时只产生一份 <see cref="MediaAsset"/>。
/// 站点路径方案参见 <c>Docs/Milestones/M3/M3.md §3.4</c>。
/// </remarks>
public sealed record MediaAsset
{
    /// <summary>源媒体文件的绝对路径（在本机文件系统中）。</summary>
    public required string SourceAbsolutePath { get; init; }

    /// <summary>站点根相对路径（以 <c>/</c> 开头、<c>/</c> 归一化），例如 <c>/media/posts/2026/hello/cover.jpg</c>。</summary>
    public required string SiteRelativePath { get; init; }

    /// <summary>SHA-256 内容指纹（小写十六进制）。</summary>
    public required string Sha256 { get; init; }

    /// <summary>MIME 类型，未知时回退到 <c>application/octet-stream</c>。</summary>
    public required string ContentType { get; init; }

    /// <summary>文件字节大小。</summary>
    public required long SizeBytes { get; init; }
}
