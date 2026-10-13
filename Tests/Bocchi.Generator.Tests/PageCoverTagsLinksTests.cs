using System.Text.Json;

using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.Generator.Tests;

/// <summary>Page 的 cover / tags / links 要从 frontmatter 进到 pages.json 与主题输入。</summary>
public sealed class PageCoverTagsLinksTests
{
    [Fact]
    public async Task FullBuild_WritesPageCoverTagsAndLinks()
    {
        using var fixture = new TestWorkspaceFixture();
        var pageDir = Path.Combine(fixture.Layout.Workspace.PagesDirectory, "about");
        Directory.CreateDirectory(Path.Combine(pageDir, "media"));
        await File.WriteAllTextAsync(
            Path.Combine(pageDir, "index.md"),
            """
            ---
            title: About
            slug: about
            status: Published
            template: about
            cover: media/portrait.jpg
            tags: [maker, design]
            links:
              - label: GitHub
                url: https://github.com/example
              - label: Blog
                url: https://example.com
            ---
            About body with portrait.
            """,
            TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            Path.Combine(pageDir, "media", "portrait.jpg"),
            [0xFF, 0xD8, 0xFF, 0xE0],
            TestContext.Current.CancellationToken);

        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);
        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Environment = "production" },
            sink,
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(BuildStatus.Succeeded);

        var pagesJson = await File.ReadAllTextAsync(
            Path.Combine(fixture.Layout.ThemeInputDirectory, "pages.json"),
            TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(pagesJson);
        var page = doc.RootElement.GetProperty("data").EnumerateArray()
            .Single(item => item.GetProperty("slug").GetString() == "about");

        page.GetProperty("template").GetString().Should().Be("about");
        page.GetProperty("tags").EnumerateArray().Select(x => x.GetString()).Should().Equal("maker", "design");
        page.GetProperty("cover").GetProperty("path").GetString().Should().Be("/media/pages/about/portrait.jpg");
        var links = page.GetProperty("links").EnumerateArray().ToArray();
        links.Should().HaveCount(2);
        links[0].GetProperty("label").GetString().Should().Be("GitHub");
        links[0].GetProperty("url").GetString().Should().Be("https://github.com/example");
        links[1].GetProperty("label").GetString().Should().Be("Blog");

        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "media", "pages", "about", "portrait.jpg"))
            .Should().BeTrue();
    }
}
