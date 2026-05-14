using System.Xml.Linq;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.Generator.Tests;

public sealed class FileSystemBuildSinkTests
{
    [Fact]
    public async Task WriteAsync_RejectsPathTraversal()
    {
        var temp = Path.Combine(Path.GetTempPath(), "bocchi-sink-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var layout = new WorkspaceLayout(temp);
            var sink = new FileSystemBuildSink(layout);

            var bad = new BuildArtifact
            {
                Path = "/../escape.txt",
                Kind = ArtifactKind.SiteArtifact,
                ContentType = "text/plain",
                SizeBytes = 0,
                Sha256 = "0",
                ProducedBy = "test",
            };
            using var stream = new MemoryStream([0]);
            var act = async () => await sink.WriteAsync(bad, stream, default);
            await act.Should().ThrowAsync<Exception>("写出路径越过 PublicRoot 必须被拒绝");
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ResolveAbsolutePath_RoutesByArtifactKind()
    {
        var temp = Path.Combine(Path.GetTempPath(), "bocchi-sink-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var layout = new WorkspaceLayout(temp);
            var sink = new FileSystemBuildSink(layout);

            var themeInput = new BuildArtifact
            {
                Path = "/posts.json",
                Kind = ArtifactKind.ThemeInput,
                ContentType = "application/json",
                SizeBytes = 1,
                Sha256 = "x",
                ProducedBy = "t",
            };
            sink.ResolveAbsolutePath(themeInput)
                .Should().StartWith(Path.GetFullPath(layout.ThemeInputDirectory));

            var site = themeInput with { Kind = ArtifactKind.SiteArtifact, Path = "/robots.txt" };
            sink.ResolveAbsolutePath(site)
                .Should().StartWith(Path.GetFullPath(layout.PublicOutputDirectory));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }
}

public sealed class SiteArtifactStructureTests
{
    [Fact]
    public async Task SitemapXml_IsValidXmlAndContainsBaseUrl()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);
        await pipeline.RunAsync(new BuildOptions { Mode = BuildMode.FullBuild }, sink, null, "0.0.0-test", default);

        var sitemap = Path.Combine(fixture.Layout.PublicOutputDirectory, "sitemap.xml");
        var doc = XDocument.Load(sitemap);
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.LocalName.Should().Be("urlset");
        doc.Root.Descendants().Should().Contain(e => e.Name.LocalName == "loc");
    }

    [Fact]
    public async Task FeedXml_IsValidAtomDocument()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);
        await pipeline.RunAsync(new BuildOptions { Mode = BuildMode.FullBuild }, sink, null, "0.0.0-test", default);

        var feed = Path.Combine(fixture.Layout.PublicOutputDirectory, "feed.xml");
        var doc = XDocument.Load(feed);
        doc.Root.Should().NotBeNull();
        doc.Root!.Name.LocalName.Should().Be("feed");
        doc.Root.Name.NamespaceName.Should().Be("http://www.w3.org/2005/Atom");
    }

    [Fact]
    public async Task RobotsTxt_StartsWithUserAgentDirective()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);
        await pipeline.RunAsync(new BuildOptions { Mode = BuildMode.FullBuild }, sink, null, "0.0.0-test", default);

        var robots = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "robots.txt"));
        robots.Should().Contain("User-agent:");
    }
}
