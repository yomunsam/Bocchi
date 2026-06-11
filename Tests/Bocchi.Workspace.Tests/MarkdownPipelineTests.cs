using Bocchi.Workspace.Content;

namespace Bocchi.Workspace.Tests;

/// <summary>MarkdownPipeline 的摘要与媒体抽取契约测试。</summary>
public sealed class MarkdownPipelineTests
{
    /// <summary>自动摘要应跳过图片段落，避免把图片 alt 或媒体标签当成正文摘要。</summary>
    [Fact]
    public void ExtractExcerpt_SkipsImageParagraphs()
    {
        var markdown = new MarkdownPipeline();
        var excerpt = markdown.ExtractExcerpt("""
            # New Post

            图片1:
            ![如果这里有说明文字的话](assets/photo.jpg)

            Gif测试:
            ![动画说明](assets/demo.gif)

            第一段真正正文，包含 [链接文字](https://example.com) 和 `code`。
            """);

        excerpt.Should().Be("第一段真正正文，包含 链接文字 和 code。");
    }

    /// <summary>只有图片段落时不生成默认摘要，交给显式 frontmatter summary / description 或列表空态处理。</summary>
    [Fact]
    public void ExtractExcerpt_ReturnsNullWhenOnlyImageParagraphsRemain()
    {
        var markdown = new MarkdownPipeline();
        var excerpt = markdown.ExtractExcerpt("""
            图片1:
            ![如果这里有说明文字的话](assets/photo.jpg)

            ![动画说明](assets/demo.gif)
            """);

        excerpt.Should().BeNull();
    }
}
