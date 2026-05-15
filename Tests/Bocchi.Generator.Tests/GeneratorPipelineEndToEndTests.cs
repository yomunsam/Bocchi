using System.Text.Json;

using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.Generator.State;
using Bocchi.Generator.Theme;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.Generator.Tests;

public sealed class GeneratorPipelineEndToEndTests
{
    [Fact]
    public async Task FullBuild_ProducesAllRequiredArtifacts_AndPersistsBuildRun()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);
        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Environment = "production" },
            sink,
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        result.Artifacts.Should().Contain(a => a.Path == "/site.json");
        result.Artifacts.Should().Contain(a => a.Path == "/posts.json");
        result.Artifacts.Should().Contain(a => a.Path == "/pages.json");
        result.Artifacts.Should().Contain(a => a.Path == "/theme-context.json");
        result.Artifacts.Should().Contain(a => a.Path == "/build-context.json");
        result.Artifacts.Should().Contain(a => a.Path == "/robots.txt");
        result.Artifacts.Should().Contain(a => a.Path == "/sitemap.xml");
        result.Artifacts.Should().Contain(a => a.Path == "/feed.xml");
        result.Artifacts.Should().Contain(a => a.Path == "/.bocchi-manifest.json");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/index.html");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/posts/index.html");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/assets/app.css");

        // 文件应该真实存在
        File.Exists(Path.Combine(fixture.Layout.ThemeInputDirectory, "site.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "robots.txt")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "sitemap.xml")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "feed.xml")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, ".bocchi-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.ThemesDirectory, "default-static", "theme.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "2025", "hello", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.css")).Should().BeTrue();

        // 媒体应被复制到 output/public/media/posts/<year>/<slug>/<file>
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "media", "posts", "2025", "hello", "c.jpg"))
            .Should().BeTrue();

        // BuildRuns 行
        var store = fixture.Services.GetRequiredService<IBuildStateStore>();
        var latest = await store.GetLatestSuccessfulRunAsync(default);
        latest.Should().NotBeNull();
        latest!.Status.Should().Be(BuildStatus.Succeeded);
        latest.Fingerprint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FullBuild_TwiceWithoutChanges_ShortCircuitsSecondRun()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink1 = new FileSystemBuildSink(fixture.Layout);
        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild }, sink1, null, "0.0.0-test", default);
        first.Status.Should().Be(BuildStatus.Succeeded);

        var sink2 = new DryRunBuildSink();
        var second = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild }, sink2, null, "0.0.0-test", default);
        second.Status.Should().Be(BuildStatus.Skipped);
        second.Fingerprint!.Value.Should().Be(first.Fingerprint!.Value);
        sink2.CapturedArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task FullBuild_WithDifferentOutputOption_DoesNotShortCircuit()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            null,
            "0.0.0-test",
            default);
        first.Status.Should().Be(BuildStatus.Succeeded);

        var second = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, FeedItemCount = 1 },
            new FileSystemBuildSink(fixture.Layout),
            null,
            "0.0.0-test",
            default);

        second.Status.Should().Be(BuildStatus.Succeeded);
        second.Fingerprint!.Value.Should().NotBe(first.Fingerprint!.Value);
    }

    [Fact]
    public async Task LiveBuild_DoesNotPersistBuildRun()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var store = fixture.Services.GetRequiredService<IBuildStateStore>();

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.Live, OnlyArtifactPath = "/site.json" },
            new DryRunBuildSink(),
            null,
            "0.0.0-test",
            default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        result.BuildRunId.Should().BeNull();
        (await store.ListRecentRunsAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task ThemeInput_HasStableEnvelopeStructure()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);
        await pipeline.RunAsync(new BuildOptions { Mode = BuildMode.FullBuild }, sink, null, "0.0.0-test", default);

        var siteJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "site.json"));
        using var doc = JsonDocument.Parse(siteJson);
        doc.RootElement.GetProperty("$schema").GetString().Should().Be("https://bocchi.local/schema/v1/site.json");
        doc.RootElement.GetProperty("contractVersion").GetString().Should().Be("1.0");
        doc.RootElement.GetProperty("generatedAt").ValueKind.Should().Be(JsonValueKind.String);
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Object);

        var contextJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var contextDoc = JsonDocument.Parse(contextJson);
        contextDoc.RootElement.GetProperty("$schema").GetString().Should().Be("https://bocchi.local/schema/v1/theme-context.json");
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("id").GetString().Should().Be("default-static");
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("config")
            .GetProperty("visual").GetProperty("accentColor").GetString().Should().Be("#E85D3A");
    }

    [Fact]
    public async Task FullBuild_WithLoadedTheme_CollectsThemeOutputIntoManifest()
    {
        using var fixture = new TestWorkspaceFixture(services => services.AddSingleton<IThemeRunner, WritingThemeRunner>());
        CreateProcessTheme(fixture, "test-theme");
        WriteThemeConfig(fixture, "test-theme", """{"visual":{"accentColor":"#E85D3A"}}""");
        Directory.CreateDirectory(fixture.Layout.PublicOutputDirectory);
        await File.WriteAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "orphan.html"), "old");

        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Environment = "production" },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "test-theme",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/index.html");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/assets/app.css");
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.css")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "orphan.html")).Should().BeFalse();

        var manifestJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, ".bocchi-manifest.json"));
        using var manifest = JsonDocument.Parse(manifestJson);
        var artifacts = manifest.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
        artifacts.Should().Contain(entry =>
            entry.GetProperty("path").GetString() == "/index.html" &&
            entry.GetProperty("kind").GetString() == nameof(ArtifactKind.ThemeOutput));

        var contextJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var context = JsonDocument.Parse(contextJson);
        context.RootElement.GetProperty("data").GetProperty("theme").GetProperty("config")
            .GetProperty("visual").GetProperty("accentColor").GetString().Should().Be("#E85D3A");
    }

    [Fact]
    public async Task FullBuild_WhenThemeConfigChanges_DoesNotShortCircuit()
    {
        using var fixture = new TestWorkspaceFixture(services => services.AddSingleton<IThemeRunner, WritingThemeRunner>());
        CreateProcessTheme(fixture, "test-theme");
        WriteThemeConfig(fixture, "test-theme", """{"visual":{"accentColor":"#111111"}}""");

        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "test-theme",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        WriteThemeConfig(fixture, "test-theme", """{"visual":{"accentColor":"#222222"}}""");
        var second = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "test-theme",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        second.Status.Should().Be(BuildStatus.Succeeded);
        second.Fingerprint!.Value.Should().NotBe(first.Fingerprint!.Value);
    }

    /// <summary>创建一个只用于测试 Theme 加载边界的 process Theme manifest。</summary>
    private static void CreateProcessTheme(TestWorkspaceFixture fixture, string themeId)
    {
        var themeRoot = Path.Combine(fixture.Layout.ThemesDirectory, themeId);
        Directory.CreateDirectory(themeRoot);
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"), $$"""
            {
              "id": "{{themeId}}",
              "name": "Test Theme",
              "version": "0.1.0",
              "contractVersion": "1.0",
              "inputDir": ".bocchi/input",
              "outputDir": "build",
              "runner": {
                "kind": "process",
                "command": "echo should-not-run"
              },
              "features": {
                "posts": true,
                "pages": true,
                "works": true,
                "notes": true,
                "friends": true,
                "photos": false,
                "search": false
              }
            }
            """);
    }

    /// <summary>写入测试 Theme 的原始配置文件，模拟 Dashboard 保存后的构建输入。</summary>
    private static void WriteThemeConfig(TestWorkspaceFixture fixture, string themeId, string json)
    {
        Directory.CreateDirectory(fixture.Layout.ThemeConfigDirectory);
        File.WriteAllText(Path.Combine(fixture.Layout.ThemeConfigDirectory, themeId + ".json"), json);
    }

    /// <summary>测试用 Theme runner，直接向 Theme 本地输出目录写入可收集文件。</summary>
    private sealed class WritingThemeRunner : IThemeRunner
    {
        public async Task RunAsync(
            ThemeRunInvocation invocation,
            Action<BuildLogLevel, string> onLog,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(invocation);
            ArgumentNullException.ThrowIfNull(onLog);

            var expectedRoot = Path.GetFullPath(Path.Combine(invocation.ThemeRoot, invocation.Manifest.OutputDir));
            Path.GetFullPath(invocation.OutputDirectoryAbsolute).Should().Be(expectedRoot);

            Directory.CreateDirectory(Path.Combine(invocation.OutputDirectoryAbsolute, "assets"));
            await File.WriteAllTextAsync(
                Path.Combine(invocation.OutputDirectoryAbsolute, "index.html"),
                "<!doctype html><html><body>theme</body></html>",
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(invocation.OutputDirectoryAbsolute, "assets", "app.css"),
                "body { color: #101012; }",
                cancellationToken);
            onLog(BuildLogLevel.Info, "test theme output written");
        }
    }
}
