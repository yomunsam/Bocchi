using System.Net;

using Bocchi.HomeServer.Components.Pages.Admin.Content;

namespace Bocchi.HomeServer.Tests;

public sealed class ContentEditorAssetPreviewRewriterTests
{
    [Fact]
    public void RewriteForDraft_RewritesLocalAssetSrcAndHref()
    {
        var html = """
            <p><img src="assets/foo.png" alt=""></p>
            <p><a href='./assets/manual.pdf'>manual</a></p>
            """;

        var rewritten = WebUtility.HtmlDecode(
            ContentEditorAssetPreviewRewriter.RewriteForDraft(html, "draft-123"));

        rewritten.Should().Contain("/Admin/Content/Assets?draft=draft-123&asset=assets%2Ffoo.png");
        rewritten.Should().Contain("/Admin/Content/Assets?draft=draft-123&asset=assets%2Fmanual.pdf");
    }

    [Fact]
    public void RewriteForContent_RewritesLocalAssetsWithContentPath()
    {
        var rewritten = WebUtility.HtmlDecode(
            ContentEditorAssetPreviewRewriter.RewriteForContent(
                """<img src="assets/foo.png" alt="">""",
                "posts/2026/hello/index.md"));

        rewritten.Should().Contain("/Admin/Content/Assets?path=posts%2F2026%2Fhello%2Findex.md&asset=assets%2Ffoo.png");
    }

    [Fact]
    public void Rewrite_KeepsRemoteRootAbsoluteAndNonAssetLinksUnchanged()
    {
        const string html = """
            <img src="/assets/root.png" alt="">
            <img src="https://cdn.example.test/foo.png" alt="">
            <a href="media/foo.png">media</a>
            <img data-src="assets/lazy.png" alt="">
            """;

        var rewritten = ContentEditorAssetPreviewRewriter.RewriteForDraft(html, "draft-123");

        rewritten.Should().Be(html);
    }
}
