using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Bocchi.Generator.ContentGraph;
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
        result.Artifacts.Should().Contain(a => a.Path == "/.nojekyll");
        result.Artifacts.Should().Contain(a => a.Path == "/sitemap.xml");
        result.Artifacts.Should().Contain(a => a.Path == "/feed.xml");
        result.Artifacts.Should().Contain(a => a.Path == "/.bocchi-manifest.json");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/index.html");
        result.Artifacts.Should().OnlyContain(a => Regex.IsMatch(a.Sha256, "^[0-9a-f]{64}$"));
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/posts/index.html");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/assets/app.css");

        // 文件应该真实存在
        File.Exists(Path.Combine(fixture.Layout.ThemeInputDirectory, "site.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "robots.txt")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, ".nojekyll")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "sitemap.xml")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "feed.xml")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, ".bocchi-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.ThemesDirectory, "default-static", "theme.json")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "2025", "hello", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "notes", "k7p9xq2m", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "notes", "2025", "0314", "1230-k7p9xq2m", "index.html")).Should().BeFalse();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.css")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.js")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "favicon.svg")).Should().BeTrue();

        // 媒体应被复制到 output/public/media/posts/<year>/<slug>/<file>
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "media", "posts", "2025", "hello", "c.jpg"))
            .Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "media", "notes", "k7p9xq2m", "note.jpg"))
            .Should().BeTrue();

        var notesJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "notes.json"));
        using (var notesDoc = JsonDocument.Parse(notesJson))
        {
            var note = notesDoc.RootElement.GetProperty("data").EnumerateArray().Single();
            note.GetProperty("id").GetString().Should().Be("k7p9xq2m");
            note.GetProperty("siteRelativeUrl").GetString().Should().Be("/notes/k7p9xq2m/");
            note.GetProperty("url").GetString().Should().Be("/notes/k7p9xq2m/");
            note.GetProperty("markdown").GetString().Should().Contain("/media/notes/k7p9xq2m/note.jpg");
            note.GetProperty("media").EnumerateArray().Single().GetProperty("path").GetString()
                .Should().Be("/media/notes/k7p9xq2m/note.jpg");
        }

        var sitemap = XDocument.Load(Path.Combine(fixture.Layout.PublicOutputDirectory, "sitemap.xml"));
        XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
        sitemap.Root!.Elements(sm + "url").Select(url => url.Element(sm + "loc")?.Value)
            .Should().Contain("http://localhost/notes/k7p9xq2m/");

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
        contextDoc.RootElement.GetProperty("data").GetProperty("build").GetProperty("mode").GetString().Should().Be("full");
        contextDoc.RootElement.GetProperty("data").GetProperty("site").GetProperty("defaultTitle").GetString().Should().Be("My Site");
        contextDoc.RootElement.GetProperty("data").GetProperty("site").GetProperty("copyrightNotice").GetString()
            .Should().Be("Copyright © 2026 My Site.");
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("id").GetString().Should().Be("default-static");
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("config")
            .GetProperty("visual").GetProperty("accentColor").GetString().Should().Be("#E85D3A");
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("config")
            .GetProperty("home").GetProperty("heroTitle").GetProperty("zh-CN").GetString()
            .Should().Be("Bocchi — 写作、\n作品[color=accent]与札记。[/color]");
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

        var postsJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "posts.json"));
        using var postsDoc = JsonDocument.Parse(postsJson);
        var post = postsDoc.RootElement.GetProperty("data").EnumerateArray().Single();
        post.GetProperty("siteRelativeUrl").GetString().Should().Be("/posts/2025/hello/");
        post.GetProperty("url").GetString().Should().Be("/posts/2025/hello/");
        post.GetProperty("id").GetString().Should().Be("posts/2025/hello@zh-CN");
        post.GetProperty("language").GetString().Should().Be("zh-CN");
        post.GetProperty("localization").GetProperty("groupId").GetString().Should().Be("posts/2025/hello");
    }

    [Fact]
    public async Task ThemeInput_IncludesContentLanguageVariants()
    {
        using var fixture = new TestWorkspaceFixture();
        var variantPath = Path.Combine(fixture.Layout.Workspace.PostsDirectory, "2025", "hello", "index.zh-TW.md");
        await File.WriteAllTextAsync(variantPath, """
            ---
            title: Hello Traditional
            slug: hello
            status: Published
            language: zh-TW
            localization:
              translationOf:
                language: zh-CN
            ---
            Body in Traditional Chinese.
            """);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var sink = new FileSystemBuildSink(fixture.Layout);

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            sink,
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var postsJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "posts.json"));
        using var postsDoc = JsonDocument.Parse(postsJson);
        var posts = postsDoc.RootElement.GetProperty("data").EnumerateArray().ToArray();
        posts.Should().HaveCount(2);

        var zhTw = posts.Single(post => post.GetProperty("language").GetString() == "zh-TW");
        zhTw.GetProperty("id").GetString().Should().Be("posts/2025/hello@zh-TW");
        zhTw.GetProperty("siteRelativeUrl").GetString().Should().Be("/zh-TW/posts/2025/hello/");
        var localization = zhTw.GetProperty("localization");
        localization.GetProperty("isTranslation").GetBoolean().Should().BeTrue();
        localization.GetProperty("sourceContentId").GetString().Should().Be("posts/2025/hello@zh-CN");
        localization.GetProperty("alternates").EnumerateArray().Should().Contain(alternate =>
            alternate.GetProperty("language").GetString() == "zh-CN" &&
            alternate.GetProperty("url").GetString() == "/posts/2025/hello/");
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
    public async Task ThemeInput_ResolvesMenuTreeAndPostCategories()
    {
        using var fixture = new TestWorkspaceFixture();
        var postFile = Path.Combine(fixture.Layout.Workspace.PostsDirectory, "2025", "hello", "index.md");
        var postRaw = await File.ReadAllTextAsync(postFile);
        await File.WriteAllTextAsync(postFile, postRaw.Replace("tags: [a, b]", "category: Tech\ntags: [a, b]", StringComparison.Ordinal));
        await File.WriteAllTextAsync(fixture.Layout.Workspace.NavigationFile, """
            items:
              - id: home
                label: i18n://common@menu.home
                target:
                  type: builtin
                  value: home
                children:
                  - id: about
                    label: About page
                    target:
                      type: page
                      value: about
                    children: []
              - id: tech
                label: Tech
                target:
                  type: postCategory
                  value: tech
                children: []
              - id: more
                label: More
                children:
                  - id: notes
                    target:
                      type: builtin
                      value: notes
                    children: []
              - id: empty-about
                label: i18n://common@menu.about
                children: []
              - id: missing-page
                label: Missing page
                target:
                  type: page
                  value: deleted-about
                children: []
            """);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions
            {
                Mode = BuildMode.FullBuild,
                PostCategories =
                [
                    new BuildCategoryNode
                    {
                        Id = "tech",
                        Name = "Tech",
                        Slug = "tech",
                    },
                ],
            },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);

        var navigationJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "navigation.json"));
        using var navigationDoc = JsonDocument.Parse(navigationJson);
        var navigation = navigationDoc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        navigation.Should().HaveCount(3);
        navigation[0].GetProperty("href").GetString().Should().Be("/");
        navigation[0].GetProperty("labelI18n").GetProperty("key").GetString().Should().Be("menu.home");
        navigation[0].GetProperty("children").EnumerateArray().Single().GetProperty("href").GetString().Should().Be("/about/");
        navigation[1].GetProperty("target").GetProperty("type").GetString().Should().Be("postCategory");
        navigation[1].GetProperty("href").GetString().Should().Be("/posts/categories/tech/");
        navigation[2].GetProperty("href").ValueKind.Should().Be(JsonValueKind.Null);
        navigation[2].GetProperty("children").EnumerateArray().Single().GetProperty("href").GetString().Should().Be("/notes/");
        navigationJson.Should().NotContain("deleted-about");

        var categoriesJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "post-categories.json"));
        using var categoriesDoc = JsonDocument.Parse(categoriesJson);
        var category = categoriesDoc.RootElement.GetProperty("data").EnumerateArray().Single();
        category.GetProperty("slug").GetString().Should().Be("tech");
        category.GetProperty("siteRelativeUrl").GetString().Should().Be("/posts/categories/tech/");
        category.GetProperty("count").GetInt32().Should().Be(1);

        var postsJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "posts.json"));
        using var postsDoc = JsonDocument.Parse(postsJson);
        postsDoc.RootElement.GetProperty("data").EnumerateArray().Single().GetProperty("categorySlug").GetString().Should().Be("tech");
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "categories", "tech", "index.html")).Should().BeTrue();
        var indexHtml = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        indexHtml.Should().Contain("<span class=\"nav__label\">More</span>");
        indexHtml.Should().Contain("<a href=\"notes/\"");
        indexHtml.Should().NotContain("empty-about");
        indexHtml.Should().NotContain("Missing page");
    }

    [Fact]
    public async Task ThemeInputAndSitemap_FlattenNestedPostCategories()
    {
        using var fixture = new TestWorkspaceFixture();
        var postFile = Path.Combine(fixture.Layout.Workspace.PostsDirectory, "2025", "hello", "index.md");
        var postRaw = await File.ReadAllTextAsync(postFile);
        await File.WriteAllTextAsync(postFile, postRaw.Replace("tags: [a, b]", "category: AI\ntags: [a, b]", StringComparison.Ordinal));
        await File.WriteAllTextAsync(fixture.Layout.Workspace.NavigationFile, """
            items:
              - id: ai
                label: AI
                target:
                  type: postCategory
                  value: ai
                children: []
            """);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions
            {
                Mode = BuildMode.FullBuild,
                PostCategories =
                [
                    new BuildCategoryNode
                    {
                        Id = "tech",
                        Name = "Tech",
                        Slug = "tech",
                        Children =
                        [
                            new BuildCategoryNode
                            {
                                Id = "ai",
                                Name = "AI",
                                Slug = "ai",
                            },
                        ],
                    },
                ],
            },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);

        var navigationJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "navigation.json"));
        using var navigationDoc = JsonDocument.Parse(navigationJson);
        navigationDoc.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Single()
            .GetProperty("href").GetString().Should().Be("/posts/categories/ai/");

        var categoriesJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "post-categories.json"));
        using var categoriesDoc = JsonDocument.Parse(categoriesJson);
        var rootCategory = categoriesDoc.RootElement.GetProperty("data").EnumerateArray().Single();
        rootCategory.GetProperty("slug").GetString().Should().Be("tech");
        var childCategory = rootCategory.GetProperty("children").EnumerateArray().Single();
        childCategory.GetProperty("slug").GetString().Should().Be("ai");
        childCategory.GetProperty("count").GetInt32().Should().Be(1);

        var sitemap = XDocument.Load(Path.Combine(fixture.Layout.PublicOutputDirectory, "sitemap.xml"));
        XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = sitemap.Root!.Elements(sm + "url").Select(url => url.Element(sm + "loc")?.Value).ToArray();
        locs.Should().Contain(loc => loc != null && loc.EndsWith("/posts/categories/tech/", StringComparison.Ordinal));
        locs.Should().Contain(loc => loc != null && loc.EndsWith("/posts/categories/ai/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefaultStaticTheme_RendersInternalUrlsRelativeToEachOutputPage()
    {
        using var fixture = new TestWorkspaceFixture();
        await File.WriteAllTextAsync(fixture.Layout.Workspace.SiteSettingsFile, """
            title: Portable Site
            defaultTitle: Portable Site
            description: Portable static output
            language: zh-CN
            timeZone: Asia/Shanghai
            baseUrl: https://yomunsam.github.io/bocchi-site-test/
            copyright: Copyright 2026 Portable Site.

            author:
              name: Author

            social: []
            defaultThemeId: default-static
            enableRss: true
            enableSitemap: true
            enableSearch: true
            """);
        await File.WriteAllTextAsync(fixture.Layout.Workspace.NavigationFile, """
            items:
              - id: home
                label: i18n://common@menu.home
                target:
                  type: builtin
                  value: home
                children: []
              - id: posts
                label: i18n://common@menu.posts
                target:
                  type: builtin
                  value: posts
                children: []
              - id: about
                label: About
                target:
                  type: page
                  value: about
                children: []
            """);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var indexHtml = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        indexHtml.Should().Contain("href=\"assets/app.css\"");
        indexHtml.Should().Contain("src=\"assets/app.js\"");
        indexHtml.Should().Contain("href=\"posts/\"");
        indexHtml.Should().Contain("href=\"feed.xml\"");
        indexHtml.Should().NotContain("href=\"/");
        indexHtml.Should().NotContain("src=\"/");

        var postsHtml = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "index.html"));
        postsHtml.Should().Contain("href=\"../assets/app.css\"");
        postsHtml.Should().Contain("src=\"../assets/app.js\"");
        postsHtml.Should().Contain("href=\"../\"");
        postsHtml.Should().Contain("href=\"./\" class=\"active\"");
        postsHtml.Should().NotContain("href=\"/");
        postsHtml.Should().NotContain("src=\"/");

        var postHtml = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "posts", "2025", "hello", "index.html"));
        postHtml.Should().Contain("href=\"../../../assets/app.css\"");
        postHtml.Should().Contain("src=\"../../../assets/app.js\"");
        postHtml.Should().Contain("src=\"../../../media/posts/2025/hello/c.jpg\"");
        postHtml.Should().Contain("href=\"../../\">←");
        postHtml.Should().NotContain("href=\"/");
        postHtml.Should().NotContain("src=\"/");
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
        await File.WriteAllTextAsync(fixture.Layout.Workspace.NavigationFile, """
            items:
              - id: home
                label: i18n://common@menu.home
                target:
                  type: builtin
                  value: home
                children: []
              - id: posts
                label: i18n://common@menu.posts
                target:
                  type: builtin
                  value: posts
                children: []
            """);

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
        visibleHtml.Should().Contain("data-bocchi-i18n=\"theme.config.home.heroTitle\" data-bocchi-i18n-format=\"inlineColor\">Bocchi — 写作");
        html.Should().Contain("""<span style="color:var(--accent)">与札记。</span>""");
        visibleHtml.Should().Contain("data-bocchi-i18n=\"theme.config.home.heroSubtitle\" data-bocchi-i18n-format=\"inlineColor\">一个安静的个人站点");
        visibleHtml.Should().Contain("data-bocchi-i18n=\"theme.config.home.tag.0\">个人站点</span>");
        visibleHtml.Should().Contain("theme.defaultStatic.homeSelectedWriting\">精选写作</span>");
        visibleHtml.Should().Contain("由测试构建。");
        visibleHtml.Should().NotContain("kanban");
        visibleHtml.Should().NotContain("myaccount");
        visibleHtml.Should().NotContain("homeHeroAccent");
        visibleHtml.Should().NotContain("homeHeroRest");
        visibleHtml.Should().Contain("""data-bocchi-language-control""");
        visibleHtml.Should().Contain("""data-bocchi-language-summary>简体中文</span>""");
        visibleHtml.Should().Contain("data-bocchi-language-option=\"zh-CN\" aria-current=\"true\"");
        visibleHtml.Should().NotContain("<small>zh-CN</small>");
        visibleHtml.Should().Contain("""data-bocchi-appearance-control""");
        visibleHtml.Should().Contain("data-bocchi-appearance-option=\"auto\" aria-current=\"true\"");
        visibleHtml.Should().Contain(">自动</span></button>");
        html.Should().Contain("<script type=\"application/json\" id=\"bocchi-i18n-data\">{\"currentLanguage\":\"zh-CN\"");
        visibleHtml.Should().NotContain(">Built with Bocchi.</span>");
    }

    [Fact]
    public async Task DefaultStaticTheme_UsesThemeHomeCopyAndSiteMetaSeparately()
    {
        using var fixture = new TestWorkspaceFixture();
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        await File.WriteAllTextAsync(fixture.Layout.Workspace.SiteSettingsFile, """
            title: Visible Site
            defaultTitle: Browser Tab Title
            description: Search engine summary
            language: zh-CN
            timeZone: Asia/Shanghai
            baseUrl: https://bocchi.example/
            copyright: Copyright © 2026 Visible Site.

            author:
              name: Author

            social: []
            defaultThemeId: default-static
            enableRss: true
            enableSitemap: true
            enableSearch: true
            """);
        WriteThemeConfig(fixture, "default-static", """
            {
              "home": {
                "heroTitle": {
                  "zh-CN": "主题首页大标题"
                },
                "heroSubtitle": {
                  "en-US": "English-only custom subtitle"
                },
                "tags": {
                  "zh-CN": ["主题标签一", "主题标签二"]
                }
              }
            }
            """);

        var result = await pipeline.RunAsync(
            new BuildOptions
            {
                Mode = BuildMode.FullBuild,
                Localization = CreateLocalizationOptions("Home", "首页") with
                {
                    PrimaryLanguage = "zh-CN",
                },
            },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        var contextJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var contextDoc = JsonDocument.Parse(contextJson);
        var homeConfig = contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("config").GetProperty("home");
        homeConfig.GetProperty("heroTitle").GetProperty("zh-CN").GetString().Should().Be("主题首页大标题");
        homeConfig.GetProperty("heroTitle").GetProperty("en-US").GetString().Should().Be("Bocchi — writing,\nwork, [color=accent]& notes.[/color]");
        homeConfig.GetProperty("heroSubtitle").GetProperty("zh-CN").GetString().Should().Be("一个安静的个人站点，用来放长文章、作品和短札记。");

        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        var visibleHtml = System.Net.WebUtility.HtmlDecode(html);
        html.Should().Contain("""<title>Browser Tab Title</title>""");
        html.Should().Contain("""<meta name="description" content="Search engine summary">""");
        visibleHtml.Should().Contain("""data-bocchi-i18n="theme.config.home.heroTitle" data-bocchi-i18n-format="inlineColor">主题首页大标题</h1>""");
        visibleHtml.Should().Contain("""data-bocchi-i18n="theme.config.home.heroSubtitle" data-bocchi-i18n-format="inlineColor">一个安静的个人站点，用来放长文章、作品和短札记。</p>""");
        visibleHtml.Should().Contain("""data-bocchi-i18n="theme.config.home.tag.0">主题标签一</span>""");
        visibleHtml.Should().NotContain("Search engine summary</p>");
        visibleHtml.Should().NotContain("kanban");
        visibleHtml.Should().NotContain("account");
        visibleHtml.Should().NotContain("admin");
        visibleHtml.Should().NotContain("myaccount");
    }

    [Fact]
    public async Task DefaultStaticTheme_RendersInlineColorMarkupOnlyForDeclaredHomeText()
    {
        using var fixture = new TestWorkspaceFixture();
        WriteThemeConfig(fixture, "default-static", """
            {
              "home": {
                "heroTitle": {
                  "zh-CN": "[color=#E85D3A]强调<script>[/color] [color=accent]Accent[/color] [color=#123]短色"
                },
                "heroSubtitle": {
                  "zh-CN": "普通 <script>alert(1)</script> [color=red]红[/color] [/color]"
                }
              }
            }
            """);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);

        var contextJson = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var contextDoc = JsonDocument.Parse(contextJson);
        contextDoc.RootElement.GetProperty("data").GetProperty("theme").GetProperty("config")
            .GetProperty("home").GetProperty("heroTitle").GetProperty("zh-CN").GetString()
            .Should().Contain("[color=#E85D3A]强调<script>[/color]");

        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        html.Should().Contain("""data-bocchi-i18n="theme.config.home.heroTitle" data-bocchi-i18n-format="inlineColor">""");
        html.Should().Contain("""<span style="color:#E85D3A">强调&lt;script&gt;</span>""");
        html.Should().Contain("""<span style="color:var(--accent)">Accent</span>""");
        html.Should().Contain("""<span style="color:#123">短色</span></h1>""");
        html.Should().Contain("""data-bocchi-i18n="theme.config.home.heroSubtitle" data-bocchi-i18n-format="inlineColor">普通 &lt;script&gt;alert(1)&lt;/script&gt; [color=red]红[/color] [/color]</p>""");
        html.Should().NotContain("""style="color:red""");
        html.Should().NotContain("""style="color:javascript""");
        html.Should().NotContain("""<script>alert(1)</script>""");

        var clientI18nMarker = """<script type="application/json" id="bocchi-i18n-data">""";
        var clientI18nStart = html.IndexOf(clientI18nMarker, StringComparison.Ordinal);
        clientI18nStart.Should().BeGreaterThanOrEqualTo(0);
        clientI18nStart += clientI18nMarker.Length;
        var clientI18nEnd = html.IndexOf("</script>", clientI18nStart, StringComparison.Ordinal);
        clientI18nEnd.Should().BeGreaterThan(clientI18nStart);
        using var clientI18nDoc = JsonDocument.Parse(html[clientI18nStart..clientI18nEnd]);
        clientI18nDoc.RootElement.GetProperty("text").GetProperty("theme.config.home.heroTitle").GetProperty("zh-CN").GetString()
            .Should().Contain("[color=#E85D3A]强调<script>[/color]");
    }

    [Fact]
    public async Task DefaultStaticTheme_MaterializesReferenceSourceAndPreservesUserFiles()
    {
        using var fixture = new TestWorkspaceFixture();
        var themeRoot = Path.Combine(fixture.Layout.ThemesDirectory, "default-static");
        var appCss = Path.Combine(themeRoot, "assets", "app.css");

        await DefaultStaticThemeDefinition.EnsureAsync(fixture.Layout.ThemesDirectory);

        var manifest = await File.ReadAllTextAsync(Path.Combine(themeRoot, "theme.json"));
        manifest.Should().Contain("\"kind\": \"fluid-static\"");
        File.Exists(Path.Combine(themeRoot, "config-schema.json")).Should().BeTrue();
        File.Exists(Path.Combine(themeRoot, "templates", "layouts", "base.liquid")).Should().BeTrue();
        File.Exists(Path.Combine(themeRoot, "templates", "pages", "index.liquid")).Should().BeTrue();
        File.Exists(appCss).Should().BeTrue();
        File.Exists(Path.Combine(themeRoot, "README.md")).Should().BeTrue();

        await File.WriteAllTextAsync(appCss, "/* user custom css */");
        await DefaultStaticThemeDefinition.EnsureAsync(fixture.Layout.ThemesDirectory);

        (await File.ReadAllTextAsync(appCss)).Should().Be("/* user custom css */");
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
    public async Task FullBuild_WithStaticAssets_CopiesAndCollectsDeclaredFiles()
    {
        using var fixture = new TestWorkspaceFixture(services => services.AddSingleton<IThemeRunner, WritingIndexOnlyThemeRunner>());
        CreateProcessTheme(fixture, "asset-theme", """
            [
              {
                "from": "static",
                "to": "/theme-static"
              }
            ]
            """);
        var themeRoot = Path.Combine(fixture.Layout.ThemesDirectory, "asset-theme");
        await WriteThemeFileAsync(themeRoot, "static/app.css", "body{}");
        await WriteThemeFileAsync(themeRoot, "static/app.js", "console.log('ok');");
        await WriteThemeFileAsync(themeRoot, "static/icon.svg", "<svg />");
        await WriteThemeFileAsync(themeRoot, "static/images/cover.png", "png");
        await WriteThemeFileAsync(themeRoot, "static/fonts/site.woff2", "font");

        var result = await fixture.Services.GetRequiredService<GeneratorPipeline>().RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "asset-theme",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/theme-static/app.css");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/theme-static/app.js");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/theme-static/icon.svg");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/theme-static/images/cover.png");
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/theme-static/fonts/site.woff2");
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "theme-static", "app.css")).Should().BeTrue();
    }

    [Fact]
    public async Task FullBuild_WithStaticAssets_RespectsIncludeAndExclude()
    {
        using var fixture = new TestWorkspaceFixture(services => services.AddSingleton<IThemeRunner, WritingIndexOnlyThemeRunner>());
        CreateProcessTheme(fixture, "asset-theme", """
            [
              {
                "from": "static",
                "to": "/assets",
                "include": ["**/*.min.css", "**/*.min.js"],
                "exclude": ["**/*.map"]
              }
            ]
            """);
        var themeRoot = Path.Combine(fixture.Layout.ThemesDirectory, "asset-theme");
        await WriteThemeFileAsync(themeRoot, "static/app.css", "body{}");
        await WriteThemeFileAsync(themeRoot, "static/app.min.css", "body{}");
        await WriteThemeFileAsync(themeRoot, "static/app.js", "console.log('dev');");
        await WriteThemeFileAsync(themeRoot, "static/app.min.js", "console.log('min');");
        await WriteThemeFileAsync(themeRoot, "static/app.min.js.map", "{}");

        var result = await fixture.Services.GetRequiredService<GeneratorPipeline>().RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "asset-theme",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.min.css")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.min.js")).Should().BeTrue();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.css")).Should().BeFalse();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.js")).Should().BeFalse();
        File.Exists(Path.Combine(fixture.Layout.PublicOutputDirectory, "assets", "app.min.js.map")).Should().BeFalse();
    }

    [Fact]
    public async Task FullBuild_WithStaticAssets_FailsWhenRunnerAlreadyWroteTarget()
    {
        using var fixture = new TestWorkspaceFixture(services => services.AddSingleton<IThemeRunner, WritingThemeRunner>());
        CreateProcessTheme(fixture, "asset-theme", """
            [
              {
                "from": "assets",
                "to": "/assets"
              }
            ]
            """);
        await WriteThemeFileAsync(Path.Combine(fixture.Layout.ThemesDirectory, "asset-theme"), "assets/app.css", "body{}");

        var result = await fixture.Services.GetRequiredService<GeneratorPipeline>().RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "asset-theme",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Failed);
        result.Reason.Should().Contain("/assets/app.css");
        result.Reason.Should().Contain("已存在");
    }

    [Fact]
    public async Task LiveBuild_WithStaticAssets_UsesLiveThemeOutputDirectory()
    {
        using var fixture = new TestWorkspaceFixture(services => services.AddSingleton<IThemeRunner, WritingIndexOnlyThemeRunner>());
        CreateProcessTheme(fixture, "asset-theme", """
            [
              {
                "from": "static",
                "to": "/theme-static"
              }
            ]
            """);
        await WriteThemeFileAsync(Path.Combine(fixture.Layout.ThemesDirectory, "asset-theme"), "static/app.css", "body{}");
        var liveInput = Path.Combine(fixture.Root, "live-input");
        var liveOutput = Path.Combine(fixture.Root, "live-output");

        var result = await fixture.Services.GetRequiredService<GeneratorPipeline>().RunAsync(
            new BuildOptions
            {
                Mode = BuildMode.Live,
                LiveThemeInputDirectory = liveInput,
                LiveThemeOutputDirectory = liveOutput,
            },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "asset-theme",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        File.Exists(Path.Combine(liveOutput, "theme-static", "app.css")).Should().BeTrue();
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/theme-static/app.css");
    }

    [Fact]
    public async Task FullBuild_WithThirdPartyFluidStaticTheme_UsesPublicRunner()
    {
        using var fixture = new TestWorkspaceFixture();
        CreateFluidStaticTheme(fixture, "fluid-reference");

        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();
        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild },
            new FileSystemBuildSink(fixture.Layout),
            themeId: "fluid-reference",
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);
        result.Artifacts.Should().Contain(a => a.Kind == ArtifactKind.ThemeOutput && a.Path == "/index.html");
        var html = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.PublicOutputDirectory, "index.html"));
        html.Should().Contain("id=\"third-party-fluid-static\"");
        html.Should().Contain("My Site");
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

    /// <summary>创建一个只用于测试 Theme 加载边界的 process Theme manifest。</summary>
    private static void CreateProcessTheme(TestWorkspaceFixture fixture, string themeId, string? staticAssetsJson = null)
    {
        var themeRoot = Path.Combine(fixture.Layout.ThemesDirectory, themeId);
        Directory.CreateDirectory(themeRoot);
        var staticAssetsBlock = string.IsNullOrWhiteSpace(staticAssetsJson)
            ? string.Empty
            : $",\n  \"staticAssets\": {staticAssetsJson}";
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
              }{{staticAssetsBlock}},
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

    private static async Task WriteThemeFileAsync(string themeRoot, string relativePath, string content)
    {
        var path = Path.Combine(themeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    /// <summary>创建一个使用公开 fluid-static runner 的第三方 Theme。</summary>
    private static void CreateFluidStaticTheme(TestWorkspaceFixture fixture, string themeId)
    {
        var themeRoot = Path.Combine(fixture.Layout.ThemesDirectory, themeId);
        Directory.CreateDirectory(Path.Combine(themeRoot, "templates", "layouts"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "templates", "pages"));
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"), $$"""
            {
              "id": "{{themeId}}",
              "name": "Fluid Reference",
              "version": "0.1.0",
              "contractVersion": "1.0",
              "inputDir": "../../cache/theme-input",
              "outputDir": "build",
              "runner": {
                "kind": "fluid-static",
                "entry": "fluid"
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
        File.WriteAllText(Path.Combine(themeRoot, "templates", "layouts", "base.liquid"), """
            <!doctype html>
            <html><body><main>{{ content | html }}</main></body></html>
            """);
        File.WriteAllText(Path.Combine(themeRoot, "templates", "pages", "index.liquid"), """
            <section id="third-party-fluid-static">{{ site.title }}</section>
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

    /// <summary>只写一个 index.html，方便 staticAssets tests 判断复制行为本身。</summary>
    private sealed class WritingIndexOnlyThemeRunner : IThemeRunner
    {
        public async Task RunAsync(
            ThemeRunInvocation invocation,
            Action<BuildLogLevel, string> onLog,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(invocation.OutputDirectoryAbsolute);
            await File.WriteAllTextAsync(
                Path.Combine(invocation.OutputDirectoryAbsolute, "index.html"),
                "<!doctype html><html><body>theme</body></html>",
                cancellationToken);
            onLog(BuildLogLevel.Info, "test theme index written");
        }
    }
}
