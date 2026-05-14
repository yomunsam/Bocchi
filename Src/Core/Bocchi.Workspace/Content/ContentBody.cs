using Bocchi.ContentModel;

namespace Bocchi.Workspace.Content;

/// <summary>
/// 一段已解析的 Markdown 正文。
/// </summary>
/// <param name="Markdown">原始 Markdown 文本（不含 frontmatter）。</param>
/// <param name="Html">渲染后的 HTML。</param>
/// <param name="Excerpt">自动摘要（首段或首 N 字）。</param>
/// <param name="ReferencedMedia">从正文中识别出来的媒体引用（相对当前文件的路径）。</param>
public sealed record ContentBody(
    string Markdown,
    string Html,
    string? Excerpt,
    IReadOnlyList<MediaReference> ReferencedMedia);