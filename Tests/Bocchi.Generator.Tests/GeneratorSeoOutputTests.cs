using System.Net;
using System.Text.Json;
using System.Xml.Linq;

using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.Generator.Tests;

public sealed class GeneratorSeoOutputTests
{
    [Fact]
    public async Task ThemeInput_ExposesMultilingualSeoFieldsForPostPageAndWorkVariants()
    {
        using var fixture = new TestWorkspaceFixture();
        await SeedMultilingualContentAsync(fixture);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptions() },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);

        var posts = await ReadThemeInputArrayAsync(fixture, "posts.json");
        var zhCnPost = posts.Single(item => item.GetProperty("id").GetString() == "posts/2025/hello@zh-CN");
        zhCnPost.GetProperty("language").GetString().Should().Be("zh-CN");
        zhCnPost.GetProperty("siteRelativeUrl").GetString().Should().Be("/posts/2025/hello/");
        zhCnPost.GetProperty("canonicalUrl").GetString().Should().Be("https://bocchi.example/posts/2025/hello/");
        var zhCnPostAlternate = zhCnPost.GetProperty("localization").GetProperty("alternates").EnumerateArray()
            .Single(alternate => alternate.GetProperty("language").GetString() == "zh-TW");
        zhCnPostAlternate.GetProperty("hreflang").GetString().Should().Be("zh-TW");
        zhCnPostAlternate.GetProperty("siteRelativeUrl").GetString().Should().Be("/zh-TW/posts/2025/hello/");
        zhCnPostAlternate.GetProperty("href").GetString().Should().Be("https://bocchi.example/zh-TW/posts/2025/hello/");

        var zhTwPost = posts.Single(item => item.GetProperty("id").GetString() == "posts/2025/hello@zh-TW");
        zhTwPost.GetProperty("language").GetString().Should().Be("zh-TW");
        zhTwPost.GetProperty("siteRelativeUrl").GetString().Should().Be("/zh-TW/posts/2025/hello/");
        zhTwPost.GetProperty("canonicalUrl").GetString().Should().Be("https://bocchi.example/zh-TW/posts/2025/hello/");
        var postAlternate = zhTwPost.GetProperty("localization").GetProperty("alternates").EnumerateArray()
            .Single(alternate => alternate.GetProperty("language").GetString() == "zh-CN");
        postAlternate.GetProperty("hreflang").GetString().Should().Be("zh-CN");
        postAlternate.GetProperty("siteRelativeUrl").GetString().Should().Be("/posts/2025/hello/");
        postAlternate.GetProperty("href").GetString().Should().Be("https://bocchi.example/posts/2025/hello/");

        var pages = await ReadThemeInputArrayAsync(fixture, "pages.json");
        var zhCnPage = pages.Single(item => item.GetProperty("id").GetString() == "pages/about@zh-CN");
        zhCnPage.GetProperty("language").GetString().Should().Be("zh-CN");
        zhCnPage.GetProperty("siteRelativeUrl").GetString().Should().Be("/about/");
        zhCnPage.GetProperty("canonicalUrl").GetString().Should().Be("https://bocchi.example/about/");
        zhCnPage.GetProperty("localization").GetProperty("alternates").EnumerateArray().Should().Contain(alternate =>
            alternate.GetProperty("hreflang").GetString() == "zh-TW" &&
            alternate.GetProperty("siteRelativeUrl").GetString() == "/zh-TW/about/" &&
            alternate.GetProperty("href").GetString() == "https://bocchi.example/zh-TW/about/");
        var zhTwPage = pages.Single(item => item.GetProperty("id").GetString() == "pages/about@zh-TW");
        zhTwPage.GetProperty("language").GetString().Should().Be("zh-TW");
        zhTwPage.GetProperty("siteRelativeUrl").GetString().Should().Be("/zh-TW/about/");
        zhTwPage.GetProperty("canonicalUrl").GetString().Should().Be("https://bocchi.example/zh-TW/about/");
        zhTwPage.GetProperty("localization").GetProperty("alternates").EnumerateArray().Should().Contain(alternate =>
            alternate.GetProperty("hreflang").GetString() == "zh-CN" &&
            alternate.GetProperty("siteRelativeUrl").GetString() == "/about/" &&
            alternate.GetProperty("href").GetString() == "https://bocchi.example/about/");

        var works = await ReadThemeInputArrayAsync(fixture, "works.json");
        var zhCnWork = works.Single(item => item.GetProperty("id").GetString() == "works/2026/portfolio@zh-CN");
        zhCnWork.GetProperty("language").GetString().Should().Be("zh-CN");
        zhCnWork.GetProperty("siteRelativeUrl").GetString().Should().Be("/works/2026/portfolio/");
        zhCnWork.GetProperty("canonicalUrl").GetString().Should().Be("https://bocchi.example/works/2026/portfolio/");
        zhCnWork.GetProperty("localization").GetProperty("alternates").EnumerateArray().Should().Contain(alternate =>
            alternate.GetProperty("hreflang").GetString() == "zh-TW" &&
            alternate.GetProperty("siteRelativeUrl").GetString() == "/zh-TW/works/2026/portfolio/" &&
            alternate.GetProperty("href").GetString() == "https://bocchi.example/zh-TW/works/2026/portfolio/");
        var zhTwWork = works.Single(item => item.GetProperty("id").GetString() == "works/2026/portfolio@zh-TW");
        zhTwWork.GetProperty("language").GetString().Should().Be("zh-TW");
        zhTwWork.GetProperty("siteRelativeUrl").GetString().Should().Be("/zh-TW/works/2026/portfolio/");
        zhTwWork.GetProperty("canonicalUrl").GetString().Should().Be("https://bocchi.example/zh-TW/works/2026/portfolio/");
        zhTwWork.GetProperty("localization").GetProperty("sourceLanguage").GetString().Should().Be("zh-CN");
        zhTwWork.GetProperty("localization").GetProperty("sourceContentId").GetString().Should().Be("works/2026/portfolio@zh-CN");

        var navigation = await ReadNavigationItemsAsync(fixture);
        navigation.Single().GetProperty("href").GetString().Should().Be("/about/");
    }

    [Fact]
    public async Task SitemapAndDefaultStaticTheme_RenderMultilingualSeoLinks()
    {
        using var fixture = new TestWorkspaceFixture();
        await SeedMultilingualContentAsync(fixture);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptions() },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);

        var sitemap = XDocument.Load(Path.Combine(fixture.Layout.PublicOutputDirectory, "sitemap.xml"));
        XNamespace sm = "http://www.sitemaps.org/schemas/sitemap/0.9";
        XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        var locs = sitemap.Root!.Elements(sm + "url").Select(url => url.Element(sm + "loc")?.Value).ToArray();
        locs.Should().Contain("https://bocchi.example/posts/2025/hello/");
        locs.Should().Contain("https://bocchi.example/zh-TW/posts/2025/hello/");
        locs.Should().Contain("https://bocchi.example/about/");
        locs.Should().Contain("https://bocchi.example/zh-TW/about/");
        locs.Should().Contain("https://bocchi.example/works/2026/portfolio/");
        locs.Should().Contain("https://bocchi.example/zh-TW/works/2026/portfolio/");
        var zhTwPost = sitemap.Root.Elements(sm + "url")
            .Single(url => url.Element(sm + "loc")?.Value == "https://bocchi.example/zh-TW/posts/2025/hello/");
        zhTwPost.Elements(xhtml + "link").Should().Contain(link =>
            (string?)link.Attribute("rel") == "alternate" &&
            (string?)link.Attribute("hreflang") == "zh-CN" &&
            (string?)link.Attribute("href") == "https://bocchi.example/posts/2025/hello/");
        zhTwPost.Elements(xhtml + "link").Should().Contain(link =>
            (string?)link.Attribute("hreflang") == "zh-TW" &&
            (string?)link.Attribute("href") == "https://bocchi.example/zh-TW/posts/2025/hello/");
        var zhTwPage = sitemap.Root.Elements(sm + "url")
            .Single(url => url.Element(sm + "loc")?.Value == "https://bocchi.example/zh-TW/about/");
        zhTwPage.Elements(xhtml + "link").Should().Contain(link =>
            (string?)link.Attribute("hreflang") == "zh-CN" &&
            (string?)link.Attribute("href") == "https://bocchi.example/about/");
        var zhTwWork = sitemap.Root.Elements(sm + "url")
            .Single(url => url.Element(sm + "loc")?.Value == "https://bocchi.example/zh-TW/works/2026/portfolio/");
        zhTwWork.Elements(xhtml + "link").Should().Contain(link =>
            (string?)link.Attribute("hreflang") == "zh-CN" &&
            (string?)link.Attribute("href") == "https://bocchi.example/works/2026/portfolio/");

        var html = await File.ReadAllTextAsync(Path.Combine(
            fixture.Layout.PublicOutputDirectory,
            "zh-TW",
            "posts",
            "2025",
            "hello",
            "index.html"));
        html.Should().Contain("""<html lang="zh-TW">""");
        html.Should().Contain("""<link rel="canonical" href="https://bocchi.example/zh-TW/posts/2025/hello/">""");
        html.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://bocchi.example/posts/2025/hello/">""");
        html.Should().Contain("""<link rel="alternate" hreflang="zh-TW" href="https://bocchi.example/zh-TW/posts/2025/hello/">""");

        var pageHtml = await File.ReadAllTextAsync(Path.Combine(
            fixture.Layout.PublicOutputDirectory,
            "zh-TW",
            "about",
            "index.html"));
        pageHtml.Should().Contain("""<html lang="zh-TW">""");
        pageHtml.Should().Contain("""<link rel="canonical" href="https://bocchi.example/zh-TW/about/">""");
        pageHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://bocchi.example/about/">""");

        var workHtml = await File.ReadAllTextAsync(Path.Combine(
            fixture.Layout.PublicOutputDirectory,
            "zh-TW",
            "works",
            "2026",
            "portfolio",
            "index.html"));
        workHtml.Should().Contain("""<html lang="zh-TW">""");
        workHtml.Should().Contain("""<link rel="canonical" href="https://bocchi.example/zh-TW/works/2026/portfolio/">""");
        workHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://bocchi.example/works/2026/portfolio/">""");
    }

    [Fact]
    public async Task DefaultStaticTheme_RendersContentLanguageSwitcherAndTranslationNotice()
    {
        using var fixture = new TestWorkspaceFixture();
        await SeedMultilingualContentAsync(fixture);
        await SeedSingleLanguagePageAsync(fixture);
        var pipeline = fixture.Services.GetRequiredService<GeneratorPipeline>();

        var result = await pipeline.RunAsync(
            new BuildOptions { Mode = BuildMode.FullBuild, Localization = CreateLocalizationOptions() },
            new FileSystemBuildSink(fixture.Layout),
            themeId: null,
            bocchiVersion: "0.0.0-test",
            cancellationToken: default);

        result.Status.Should().Be(BuildStatus.Succeeded);

        var themeContext = await ReadThemeContextAsync(fixture);
        var switchDefaults = ReadThemeI18nDefaults(themeContext, "theme.defaultStatic.languageSwitchLabel");
        switchDefaults.GetProperty("en-US").GetString().Should().Be("Read in");
        switchDefaults.GetProperty("zh-CN").GetString().Should().Be("阅读语言");
        switchDefaults.GetProperty("zh-TW").GetString().Should().Be("閱讀語言");
        switchDefaults.GetProperty("ja-JP").GetString().Should().Be("読む言語");

        var zhTwPostHtml = await File.ReadAllTextAsync(Path.Combine(
            fixture.Layout.PublicOutputDirectory,
            "zh-TW",
            "posts",
            "2025",
            "hello",
            "index.html"));
        zhTwPostHtml.Should().Contain("""data-bocchi-language-switch""");
        zhTwPostHtml.Should().Contain("data-bocchi-language-link=\"zh-CN\"");
        zhTwPostHtml.Should().Contain("data-bocchi-language-link=\"zh-TW\" aria-current=\"page\"");
        var zhTwPostText = WebUtility.HtmlDecode(zhTwPostHtml);
        zhTwPostText.Should().Contain("简体中文");
        zhTwPostText.Should().Contain("繁體中文");
        zhTwPostHtml.Should().Contain("""data-bocchi-translation-notice""");
        zhTwPostText.Should().Contain("""data-bocchi-i18n="content.translationNotice">此頁面為翻譯版本。""");
        zhTwPostText.Should().Contain("""data-bocchi-i18n="content.viewOriginal">查看原文""");
        zhTwPostHtml.Should().Contain("href=\"../../../../posts/2025/hello/\"");

        var zhTwPageHtml = await File.ReadAllTextAsync(Path.Combine(
            fixture.Layout.PublicOutputDirectory,
            "zh-TW",
            "about",
            "index.html"));
        zhTwPageHtml.Should().Contain("""data-bocchi-language-switch""");
        zhTwPageHtml.Should().Contain("""data-bocchi-translation-notice""");
        zhTwPageHtml.Should().Contain("href=\"../../about/\"");

        var zhTwWorkHtml = await File.ReadAllTextAsync(Path.Combine(
            fixture.Layout.PublicOutputDirectory,
            "zh-TW",
            "works",
            "2026",
            "portfolio",
            "index.html"));
        zhTwWorkHtml.Should().Contain("""data-bocchi-language-switch""");
        zhTwWorkHtml.Should().Contain("""data-bocchi-translation-notice""");
        zhTwWorkHtml.Should().Contain("href=\"../../../../works/2026/portfolio/\"");

        var soloHtml = await File.ReadAllTextAsync(Path.Combine(
            fixture.Layout.PublicOutputDirectory,
            "solo",
            "index.html"));
        soloHtml.Should().NotContain("""data-bocchi-language-switch""");
        soloHtml.Should().NotContain("""data-bocchi-translation-notice""");
    }

    private static async Task<JsonElement> ReadThemeContextAsync(TestWorkspaceFixture fixture)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "theme-context.json"));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
    }

    /// <summary>读取 Theme manifest 默认文案，验证四种示范语言不会在 Theme input 中丢失。</summary>
    private static JsonElement ReadThemeI18nDefaults(JsonElement themeContext, string key)
    {
        return themeContext.GetProperty("theme").GetProperty("i18n").GetProperty("keys").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == key)
            .GetProperty("defaultValues");
    }

    private static async Task<JsonElement[]> ReadThemeInputArrayAsync(TestWorkspaceFixture fixture, string fileName)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, fileName));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    /// <summary>读取 Menu tree 根节点，验证 Page 多语言 variant 不会让 navigation target 冲突。</summary>
    private static async Task<JsonElement[]> ReadNavigationItemsAsync(TestWorkspaceFixture fixture)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(fixture.Layout.ThemeInputDirectory, "navigation.json"));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    /// <summary>构造同一 group 的主语言与繁中 variant，让 URL、SEO 与 Sitemap 测试共享事实。</summary>
    private static async Task SeedMultilingualContentAsync(TestWorkspaceFixture fixture)
    {
        await File.WriteAllTextAsync(fixture.Layout.Workspace.SiteSettingsFile, """
            title: SEO Site
            defaultTitle: SEO Site
            description: Multilingual SEO test site
            language: zh-CN
            timeZone: Asia/Shanghai
            baseUrl: https://bocchi.example/
            copyright: Copyright © 2026 SEO Site.

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
              - id: about
                label: About
                target:
                  type: page
                  value: about
                children: []
            """);

        await File.WriteAllTextAsync(
            Path.Combine(fixture.Layout.Workspace.PostsDirectory, "2025", "hello", "index.zh-TW.md"),
            """
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

        await File.WriteAllTextAsync(
            Path.Combine(fixture.Layout.Workspace.PagesDirectory, "about", "index.zh-TW.md"),
            """
            ---
            title: About Traditional
            slug: about
            status: Published
            language: zh-TW
            localization:
              translationOf:
                language: zh-CN
            ---
            About body in Traditional Chinese.
            """);

        var workDir = Path.Combine(fixture.Layout.Workspace.WorksDirectory, "2026", "portfolio");
        Directory.CreateDirectory(workDir);
        await File.WriteAllTextAsync(
            Path.Combine(workDir, "index.md"),
            """
            ---
            title: Portfolio
            slug: portfolio
            status: Published
            role: Maker
            ---
            Portfolio body.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workDir, "index.zh-TW.md"),
            """
            ---
            title: Portfolio Traditional
            slug: portfolio
            status: Published
            language: zh-TW
            localization:
              translationOf:
                language: zh-CN
            ---
            Portfolio body in Traditional Chinese.
            """);
    }

    /// <summary>补一条没有同组 variant 的内容，验证默认 Theme 不输出空语言切换控件。</summary>
    private static async Task SeedSingleLanguagePageAsync(TestWorkspaceFixture fixture)
    {
        var soloDir = Path.Combine(fixture.Layout.Workspace.PagesDirectory, "solo");
        Directory.CreateDirectory(soloDir);
        await File.WriteAllTextAsync(
            Path.Combine(soloDir, "index.md"),
            """
            ---
            title: Solo
            slug: solo
            status: Published
            ---
            Solo body.
            """);
    }

    /// <summary>让 Theme Context 明确包含主语言和非主语言，供默认 Theme 输出当前页面语言。</summary>
    private static BuildLocalizationOptions CreateLocalizationOptions()
        => new()
        {
            PrimaryLanguage = "zh-CN",
            EnabledLanguages =
            [
                new BuildLanguageRecord
                {
                    Code = "zh-CN",
                    NativeName = "简体中文",
                    EnglishName = "Simplified Chinese",
                },
                new BuildLanguageRecord
                {
                    Code = "zh-TW",
                    NativeName = "繁體中文",
                    EnglishName = "Traditional Chinese",
                },
            ],
        };
}
