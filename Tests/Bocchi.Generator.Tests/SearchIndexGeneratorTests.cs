using Microsoft.Extensions.DependencyInjection;

using System.Text.Json;

using Bocchi.Generator.SiteArtifacts;

namespace Bocchi.Generator.Tests;

public sealed class SearchIndexGeneratorTests
{
    [Fact]
    public void ToPlainText_StripsTagsAndCollapsesWhitespace()
    {
        SearchIndexGenerator.ToPlainText("<p>Hello <strong>world</strong> &amp; friends</p>")
            .Should().Be("Hello world & friends");
    }

    [Fact]
    public async Task FullBuild_WritesSearchIndexCoveringPostsPagesWorksNotes()
    {
        using var fixture = new TestWorkspaceFixture();
        var pages = Path.Combine(fixture.Layout.Workspace.PagesDirectory, "about");
        Directory.CreateDirectory(pages);
        await File.WriteAllTextAsync(
            Path.Combine(pages, "index.md"),
            """
            ---
            title: About
            slug: about
            status: Published
            tags: [bio]
            ---
            About body for search.
            """,
            TestContext.Current.CancellationToken);

        var works = Path.Combine(fixture.Layout.Workspace.WorksDirectory, "2026", "demo");
        Directory.CreateDirectory(works);
        await File.WriteAllTextAsync(
            Path.Combine(works, "index.md"),
            """
            ---
            title: Demo Work
            slug: demo
            status: Published
            stack: [csharp]
            ---
            Work body for search.
            """,
            TestContext.Current.CancellationToken);

        var pipeline = fixture.Services.GetRequiredService<Bocchi.Generator.Pipeline.GeneratorPipeline>();
        var sink = new Bocchi.Generator.Sinks.FileSystemBuildSink(fixture.Layout);
        var result = await pipeline.RunAsync(
            new Bocchi.Generator.Pipeline.BuildOptions { Mode = Bocchi.Generator.Pipeline.BuildMode.FullBuild, Environment = "production" },
            sink,
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(Bocchi.Generator.Pipeline.BuildStatus.Succeeded);
        var indexPath = Path.Combine(fixture.Layout.PublicOutputDirectory, "search-index.json");
        File.Exists(indexPath).Should().BeTrue();
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken));
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        items.Select(x => x.GetProperty("kind").GetString()).Should().Contain(["post", "page", "work", "note"]);
        items.Should().Contain(x => x.GetProperty("title").GetString() == "About");
        items.Should().Contain(x => x.GetProperty("title").GetString() == "Demo Work");
        items.Should().Contain(x => x.GetProperty("title").GetString() == "Hello");
    }
}
