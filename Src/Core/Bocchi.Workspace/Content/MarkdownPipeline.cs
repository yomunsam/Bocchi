using System.Text;
using System.Text.RegularExpressions;

using Bocchi.ContentModel;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Bocchi.Workspace.Content;

/// <summary>
/// 统一的 Markdown 渲染管线。封装 Markdig 配置，提供 HTML 渲染、摘要抽取与媒体引用收集。
/// </summary>
public sealed partial class MarkdownPipeline
{
    private readonly Markdig.MarkdownPipeline _pipeline;

    public MarkdownPipeline()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoLinks()
            .UseEmojiAndSmiley()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();
    }

    /// <summary>把 Markdown 渲染为 HTML 字符串。</summary>
    public string RenderHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdown.ToHtml(markdown, _pipeline);
    }

    /// <summary>
    /// 解析 Markdown 为 AST（Markdig <see cref="MarkdownDocument"/>），供后续抽取使用。
    /// </summary>
    public MarkdownDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Markdown.Parse(markdown, _pipeline);
    }

    /// <summary>
    /// 抽取摘要：取第一个不含图片的段落纯文本，最多 <paramref name="maxChars"/> 字符。返回 <c>null</c> 表示无内容。
    /// </summary>
    public string? ExtractExcerpt(string markdown, int maxChars = 160)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (maxChars <= 0)
        {
            return null;
        }

        var doc = Parse(markdown);
        foreach (var block in doc)
        {
            if (block is ParagraphBlock paragraph && paragraph.Inline is not null)
            {
                if (ContainsImage(paragraph.Inline))
                {
                    continue;
                }

                var text = ExtractInlineText(paragraph.Inline, includeImages: false);
                text = WhitespaceRegex().Replace(text, " ").Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                return text.Length <= maxChars ? text : text[..maxChars] + "…";
            }
        }

        return null;
    }

    /// <summary>
    /// 收集 Markdown 中的所有媒体引用：图片（<c>![alt](path)</c>）与显式 <c>&lt;img&gt;</c> 标签。
    /// 路径保持原样（通常相对当前文件），由生成器在 M3 阶段做改写。
    /// </summary>
    public IReadOnlyList<MediaReference> ExtractMediaReferences(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        var doc = Parse(markdown);
        var list = new List<MediaReference>();
        WalkMedia(doc, list);
        return list;
    }

    private static void WalkMedia(IEnumerable<MarkdownObject> nodes, List<MediaReference> sink)
    {
        foreach (var node in nodes)
        {
            if (node is LinkInline link && link.IsImage && !string.IsNullOrWhiteSpace(link.Url))
            {
                var alt = ExtractInlineText(link);
                sink.Add(new MediaReference(link.Url!, string.IsNullOrEmpty(alt) ? null : alt));
            }

            if (node is ContainerInline ci)
            {
                WalkMedia(ci, sink);
            }
            else if (node is LeafBlock lb && lb.Inline is not null)
            {
                WalkMedia(lb.Inline, sink);
            }
            else if (node is ContainerBlock cb)
            {
                WalkMedia(cb, sink);
            }
        }
    }

    private static string ExtractInlineText(ContainerInline inline, bool includeImages = true)
    {
        var sb = new StringBuilder();
        AppendInlineText(inline, sb, includeImages);
        return sb.ToString();
    }

    private static void AppendInlineText(IEnumerable<MarkdownObject> nodes, StringBuilder sb, bool includeImages)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case LinkInline { IsImage: true } when !includeImages:
                    break;
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
                case ContainerInline ci:
                    AppendInlineText(ci, sb, includeImages);
                    break;
            }
        }
    }

    private static bool ContainsImage(IEnumerable<MarkdownObject> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is LinkInline { IsImage: true })
            {
                return true;
            }

            if (node is ContainerInline container && ContainsImage(container))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
