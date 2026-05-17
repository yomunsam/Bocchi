using System.Text.Json;

using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.Generator.State;
using Bocchi.Generator.Theme;
using Bocchi.Theme.DefaultStatic;

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
        doc.RootElement.GetProperty("data").GetProperty("defaultTitle").GetString().Should().Be("My Site");
        doc.RootElement.GetProperty("data").GetProperty("copyrightNotice").GetString().Should().Be("Copyright © 2026 My Site.");

        var contextJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var contextDoc = JsonDocument.Parse(contextJson);
        contextDoc.RootElement.GetProperty("$schema").GetString().Should().Be("https://bocchi.local/schema/v1/theme-context.json");
        contextDoc.RootElement.GetProperty("data").GetProperty("site").GetProperty("defaultTitle").GetString().Should().Be("My Site");
        contextDoc.RootElement.GetProperty("data").GetProperty("site").GetProperty("copyrightNotice").GetString()
            .Should().Be("Copyright © 2026 My Site.");
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("id").GetString().Should().Be("default-static");
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("config")
            .GetProperty("visual").GetProperty("accentColor").GetString().Should().Be("#E85D3A");
        var themeI18n = contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("i18n");
        themeI18n.GetProperty("supportedLanguages").EnumerateArray().Should()
            .Contain(language => language.GetString() == "zh-CN");
        themeI18n.GetProperty("keys").EnumerateArray().Should().Contain(key =>
            key.GetProperty("key").GetString() == "theme.defaultStatic.colophonBuiltWith"
            && key.GetProperty("defaultValues").GetProperty("zh-CN").GetString() == "由 Bocchi 构建。");
        var localization = contextDoc.RootElement.GetProperty("data").GetProperty("localization");
        localization.GetProperty("primaryLanguage").GetString().Should().Be("zh-CN");
        localization.GetProperty("urlPolicy").GetString().Should().Be("PrimaryUnprefixed");
        localization.GetProperty("enabledLanguages").EnumerateArray().Should().ContainSingle(
            language => language.GetProperty("code").GetString() == "zh-CN"
                && language.GetProperty("nativeName").GetString() == "简体中文"
                && language.GetProperty("englishName").GetString() == "Simplified Chinese");
        localization.GetProperty("text").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task ThemeInput_IncludesLocalizationSnapshotTextOverrides()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);
        var localizationOptions = CreateLocalizationOptions("Home", "首页");

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = localizationOptions },
            sink,
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var contextJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var contextDoc = JsonDocument.Parse(contextJson);
        var localization = contextDoc.RootElement.GetProperty("data").GetProperty("localization");
        localization.GetProperty("primaryLanguage").GetString().Should().Be("en-US");
        localization.GetProperty("urlPolicy").GetString().Should().Be("PrimaryUnprefixed");
        localization.GetProperty("enabledLanguages").EnumerateArray().Should().Contain(language =>
            language.GetProperty("code").GetString() == "zh-CN"
            && language.GetProperty("nativeName").GetString() == "简体中文");
        var text = localization.GetProperty("text").GetProperty("menu.home");
        text.GetProperty("en-US").GetString().Should().Be("Home");
        text.GetProperty("zh-CN").GetString().Should().Be("首页");
    }

    [Fact]
    public async Task FullBuild_WhenLocalizationTextChanges_DoesNotShortCircuit()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptions("Home", "首页") },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);
        first.Status.Should().Be(BuildStatus.Succeeded);

        var second = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptions("Start", "开始") },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        second.Status.Should().Be(BuildStatus.Succeeded);
        second.Fingerprint!.Value.Should().NotBe(first.Fingerprint!.Value);
    }

    [Fact]
    public async Task ThemeInput_MergesThemePrivateTextOverridesOverCommonText()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var localizationOptions = CreateLocalizationOptions("Home", "首页") with
        {
            Text = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["theme.defaultStatic.colophonBuiltWith"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en-US"] = "Common fallback",
                },
            },
            ThemeTextOverrides = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["theme.defaultStatic.colophonBuiltWith"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en-US"] = "Powered quietly",
                },
            },
        };

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = localizationOptions },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var contextJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var contextDoc = JsonDocument.Parse(contextJson);
        var text = contextDoc.RootElement
            .GetProperty("data")
            .GetProperty("localization")
            .GetProperty("text")
            .GetProperty("theme.defaultStatic.colophonBuiltWith");
        text.GetProperty("en-US").GetString().Should().Be("Powered quietly");
    }

    [Fact]
    public async Task DefaultStaticTheme_LocalizesChromeFromThemeContext()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var localizationOptions = CreateLocalizationOptions("Index", "首页") with
        {
            PrimaryLanguage = "zh-CN",
            ThemeTextOverrides = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["theme.defaultStatic.colophonBuiltWith"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["zh-CN"] = "由测试构建。",
                },
            },
        };

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = localizationOptions },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        var visibleHtml = System.Net.WebUtility.HtmlDecode(html);
        visibleHtml.Should().Contain("""<html lang="zh-CN">""");
        visibleHtml.Should().Contain(">首页</a>");
        visibleHtml.Should().Contain(">写作</a>");
        visibleHtml.Should().Contain("theme.defaultStatic.homeHeroAccent\">写作</em>");
        visibleHtml.Should().Contain("theme.defaultStatic.homeHeroRest\">、作品与札记。</span>");
        visibleHtml.Should().Contain("theme.defaultStatic.homeSelectedWriting\">精选写作</h2>");
        visibleHtml.Should().Contain("由测试构建。");
        visibleHtml.Should().Contain("""data-bocchi-language-control""");
        visibleHtml.Should().Contain("data-bocchi-language-option=\"zh-CN\" aria-current=\"true\"");
        visibleHtml.Should().Contain("""data-bocchi-appearance-control""");
        visibleHtml.Should().Contain("data-bocchi-appearance-option=\"auto\" aria-current=\"true\"");
        visibleHtml.Should().Contain(">自动</span></button>");
        html.Should().Contain("<script type=\"application/json\" id=\"bocchi-i18n-data\">{\"currentLanguage\":\"zh-CN\"");
        visibleHtml.Should().NotContain(">Built with Bocchi.</span>");
    }

    [Fact]
    public async Task DefaultStaticTheme_RefreshesUnmodifiedLegacyMaterializedFiles()
    {
        using var fixture = new TestWorkspaceFixture();
        var themeRoot = Path.Combine(fixture.Layout.ThemesDirectory, "default-static");
        var indexTemplate = Path.Combine(themeRoot, "templates", "pages", "index.liquid");
        var appCss = Path.Combine(themeRoot, "assets", "app.css");
        var appJs = Path.Combine(themeRoot, "assets", "app.js");
        Directory.CreateDirectory(Path.GetDirectoryName(indexTemplate)!);
        Directory.CreateDirectory(Path.GetDirectoryName(appCss)!);
        await File.WriteAllTextAsync(Path.Combine(themeRoot, "theme.json"), GetDefaultStaticPrivateConstant("LegacyThemeJson"));
        await File.WriteAllTextAsync(indexTemplate, GetDefaultStaticPrivateConstant("LegacyIndexTemplate"));
        await File.WriteAllTextAsync(appCss, GetDefaultStaticAssetsPrivateConstant("LegacyCss"));
        await File.WriteAllTextAsync(appJs, GetDefaultStaticAssetsPrivateConstant("LegacyJs"));

        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptions("Index", "首页") with { PrimaryLanguage = "zh-CN" } },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var refreshedManifest = await File.ReadAllTextAsync(Path.Combine(themeRoot, "theme.json"));
        var refreshedIndex = await File.ReadAllTextAsync(indexTemplate);
        var refreshedCss = await File.ReadAllTextAsync(appCss);
        var refreshedJs = await File.ReadAllTextAsync(appJs);
        refreshedManifest.Should().Contain("theme.defaultStatic.homeHeroAccent");
        refreshedIndex.Should().Contain("text.homeHeroAccent");
        refreshedCss.Should().Contain(".theme-menu");
        refreshedJs.Should().Contain("bocchi-theme-language");
    }

    [Fact]
    public async Task FullBuild_WhenThemePrivateLocalizationTextChanges_DoesNotShortCircuit()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptionsWithThemeText("Powered quietly") },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);
        first.Status.Should().Be(BuildStatus.Succeeded);

        var second = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptionsWithThemeText("Made with Bocchi") },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        second.Status.Should().Be(BuildStatus.Succeeded);
        second.Fingerprint!.Value.Should().NotBe(first.Fingerprint!.Value);
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

    [Fact]
    public async Task FullBuild_WhenDefaultThemeTemplateChanges_DoesNotShortCircuit()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var first = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        var indexTemplate = Path.Combine(fixture.Layout.ThemesDirectory, "default-static", "templates", "pages", "index.liquid");
        await File.WriteAllTextAsync(indexTemplate, """<section id="theme-template-changed">Changed template</section>""");

        var second = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        second.Status.Should().Be(BuildStatus.Succeeded);
        second.Fingerprint!.Value.Should().NotBe(first.Fingerprint!.Value);
        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        html.Should().Contain("theme-template-changed");
    }

    [Fact]
    public async Task DefaultStaticTheme_ExecutesWorkspaceFluidTemplate()
    {
        using var fixture = new TestWorkspaceFixture();
        var pageTemplateDirectory = Path.Combine(fixture.Layout.ThemesDirectory, "default-static", "templates", "pages");
        Directory.CreateDirectory(pageTemplateDirectory);
        await File.WriteAllTextAsync(Path.Combine(pageTemplateDirectory, "index.liquid"), """
            <section id="fluid-override">
              {% for item in featuredPosts %}<span>{{ item.title }}</span>{% endfor %}
            </section>
            """);

        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        html.Should().Contain("""<section id="fluid-override">""");
        html.Should().Contain("<span>Hello</span>");
    }

    [Fact]
    public async Task DefaultStaticTheme_RendersBodyHtmlThroughExplicitHtmlFilter()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "2025", "hello", "index.html"));
        html.Should().Contain("<p>Body <img");
        html.Should().NotContain("&lt;p&gt;Body");
    }

    /// <summary>创建带 Common i18n 文本覆盖的构建快照，避免测试重复搭建同一组语言。</summary>
    private static BuildLocalizationOptions CreateLocalizationOptions(string englishHome, string chineseHome)
        => new()
        {
            PrimaryLanguage = "en-US",
            EnabledLanguages =
            [
                new BuildLanguageRecord
                {
                    Code = "en-US",
                    NativeName = "English",
                    EnglishName = "English",
                },
                new BuildLanguageRecord
                {
                    Code = "zh-CN",
                    NativeName = "简体中文",
                    EnglishName = "Simplified Chinese",
                },
            ],
            Text = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["menu.home"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en-US"] = englishHome,
                    ["zh-CN"] = chineseHome,
                },
            },
        };

    /// <summary>创建带 Theme 私有 i18n 覆盖的构建快照，用来验证覆盖优先级与指纹。</summary>
    private static BuildLocalizationOptions CreateLocalizationOptionsWithThemeText(string englishText)
        => CreateLocalizationOptions("Home", "首页") with
        {
            ThemeTextOverrides = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["theme.defaultStatic.colophonBuiltWith"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["en-US"] = englishText,
                },
            },
        };

    /// <summary>读取默认 Theme 定义里的私有常量，供升级测试复用旧内置模板而不在测试中复制大段文本。</summary>
    private static string GetDefaultStaticPrivateConstant(string name)
    {
        var field = typeof(DefaultStaticThemeDefinition).GetField(
            name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return field?.GetRawConstantValue() as string
            ?? throw new InvalidOperationException($"DefaultStaticThemeDefinition 缺少私有常量 '{name}'。");
    }

    /// <summary>读取默认 Theme asset 定义里的私有常量，供旧物化资产刷新测试复用。</summary>
    private static string GetDefaultStaticAssetsPrivateConstant(string name)
    {
        var type = typeof(DefaultStaticThemeDefinition).Assembly.GetType("Bocchi.Theme.DefaultStatic.DefaultStaticThemeAssets")
            ?? throw new InvalidOperationException("DefaultStaticThemeAssets 类型不存在。");
        var field = type.GetField(
            name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return field?.GetRawConstantValue() as string
            ?? throw new InvalidOperationException($"DefaultStaticThemeAssets 缺少私有常量 '{name}'。");
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
              "inputDir": "../../cache/theme-input",
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
