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

    [Fact]
    public void FluidStaticRuntime_DefinesSafeSearchHrefGuard()
    {
        var jsPath = FindFluidStaticRuntime();
        var js = File.ReadAllText(jsPath);
        js.Should().Contain("isSafeSearchHref");
        js.Should().Contain("Only allow site-relative paths");
        js.Should().NotContain("link.href = hit.url || \"/\"");
    }

    private static string FindFluidStaticRuntime()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Src", "Themes", "Bocchi.Theme.FluidStatic", "Runtime", "fluid-static-v1.js");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("fluid-static-v1.js not found from " + AppContext.BaseDirectory);
    }


    [Fact]
    public async Task FullBuild_WhenSiteSearchDisabled_OmitsIndexAndSearchUi()
    {
        using var fixture = new TestWorkspaceFixture();
        var siteYaml = Path.Combine(fixture.Layout.Workspace.SiteSettingsFile);
        var yaml = await File.ReadAllTextAsync(siteYaml, TestContext.Current.CancellationToken);
        yaml = yaml.Replace("enableSearch: true", "enableSearch: false", StringComparison.Ordinal);
        await File.WriteAllTextAsync(siteYaml, yaml, TestContext.Current.CancellationToken);

        var pipeline = fixture.Services.GetRequiredService<Bocchi.Generator.Pipeline.GeneratorPipeline>();
        var sink = new Bocchi.Generator.Sinks.FileSystemBuildSink(fixture.Layout);
        var result = await pipeline.RunAsync(
            new Bocchi.Generator.Pipeline.BuildOptions { Mode = Bocchi.Generator.Pipeline.BuildMode.FullBuild, Environment = "production" },
            sink,
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.Should().Be(Bocchi.Generator.Pipeline.BuildStatus.Succeeded);
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "search-index.json")).Should().BeFalse();

        var themeContext = await File.ReadAllTextAsync(
            Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"),
            TestContext.Current.CancellationToken);
        using (var doc = JsonDocument.Parse(themeContext))
        {
            doc.RootElement.GetProperty("data").GetProperty("features").GetProperty("search").GetBoolean().Should().BeFalse();
        }

        var indexHtml = await File.ReadAllTextAsync(
            Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"),
            TestContext.Current.CancellationToken);
        indexHtml.Should().NotContain("data-bocchi-search");
    }

}
