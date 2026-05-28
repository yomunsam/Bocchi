using System.Text.Json;

using Bocchi.GeneratorContract;
using Bocchi.Theme.DefaultStatic;

namespace Bocchi.Generator.Tests;

/// <summary>默认静态 Theme renderer 的窄集成测试，直接喂 Theme Contract 输入以保护模板模型和输出写入边界。</summary>
public sealed class DefaultStaticTemplateRendererTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>验证默认 Theme 的 inline text 渲染只开放受控 color 语法，其余 Markdown / HTML 都保持普通文本语义。</summary>
    [Fact]
    public async Task RenderAsync_RendersInlineColorTextWithoutTrustingMarkdownOrHtml()
    {
        var context = CreateThemeContext(
            heroTitle: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["zh-CN"] = "Plain <b>tag</b> & [link](https://example.com) [color=accent]Accent[/color] [color=#123ABC]Hex[/color] [color=accent]open [color=#123]nested[/color] tail",
            },
            heroSubtitle: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["zh-CN"] = "普通 <em>x</em> [link](https://example.com) [color=red]红[/color] [color=javascript:alert(1)]bad[/color] [color=accent][/color] [/color]",
            });

        var html = await RenderIndexAsync(context);
        var visibleHtml = System.Net.WebUtility.HtmlDecode(html);

        html.Should().Contain("""Plain &lt;b&gt;tag&lt;/b&gt; &amp; [link](https://example.com)""");
        html.Should().Contain("""<span style="color:var(--accent)">Accent</span>""");
        html.Should().Contain("""<span style="color:#123ABC">Hex</span>""");
        html.Should().Contain("""<span style="color:var(--accent)">open <span style="color:#123">nested</span> tail</span></h1>""");
        visibleHtml.Should().Contain("""普通 <em>x</em> [link](https://example.com) [color=red]红[/color] [color=javascript:alert(1)]bad[/color] <span style="color:var(--accent)"></span> [/color]</p>""");
        html.Should().NotContain("""style="color:red""");
        html.Should().NotContain("""style="color:javascript""");
        html.Should().NotContain("<b>tag</b>");
        html.Should().NotContain("<em>x</em>");
        html.Should().NotContain("<a href=\"https://example.com");
    }

    /// <summary>验证默认 Theme 文案解析的覆盖、manifest 默认值、语言回退和缺失 key 回退行为。</summary>
    [Fact]
    public async Task RenderAsync_ResolvesThemeTextOverridesDefaultsAndFallbacks()
    {
        var context = CreateThemeContext(
            localizationText: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["menu.home"] = CreateLanguageText(("zh-CN", "自定义首页")),
                ["theme.defaultStatic.colophonBuiltWith"] = CreateLanguageText(("zh-CN", "主题私有覆盖")),
            },
            themeI18nKeys:
            [
                CreateThemeI18nKey("theme.defaultStatic.colophonBuiltWith", ("zh-CN", "Manifest 页脚"), ("en-US", "Manifest footer")),
                CreateThemeI18nKey("theme.defaultStatic.homeSelectedWriting", ("zh-CN", "Manifest 精选写作"), ("en-US", "Manifest selected writing")),
                CreateThemeI18nKey("theme.defaultStatic.homeSelectedWork", ("en-US", "Manifest selected work")),
            ]);

        var html = await RenderIndexAsync(context);
        var visibleHtml = System.Net.WebUtility.HtmlDecode(html);

        visibleHtml.Should().Contain("""data-bocchi-i18n="menu.home">自定义首页</span>""");
        visibleHtml.Should().Contain("""data-bocchi-i18n="theme.defaultStatic.colophonBuiltWith">主题私有覆盖</span>""");
        visibleHtml.Should().Contain("""data-bocchi-i18n="theme.defaultStatic.homeSelectedWriting">Manifest 精选写作</span>""");
        visibleHtml.Should().Contain("""data-bocchi-i18n="theme.defaultStatic.homeSelectedWork">Manifest selected work</span>""");
        visibleHtml.Should().Contain("""data-bocchi-i18n="theme.defaultStatic.emptyList">theme.defaultStatic.emptyList</div>""");
        visibleHtml.Should().NotContain("Manifest 页脚");
        visibleHtml.Should().NotContain("Built with Bocchi.");
    }

    /// <summary>验证自定义导航 Common i18n key 会进入前台客户端语言包，浏览器切换语言时不会停留在首屏文案。</summary>
    [Fact]
    public async Task RenderAsync_IncludesNavigationI18nKeysInClientTextJson()
    {
        var context = CreateThemeContext(
            localizationText: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["menu.custom.about-551516ab"] = CreateLanguageText(("zh-CN", "关于"), ("en-US", "About")),
            },
            includeEnglish: true);

        var html = await RenderIndexAsync(context, CreateNavigationWithCustomI18nLabel());
        var visibleHtml = System.Net.WebUtility.HtmlDecode(html);

        visibleHtml.Should().Contain("""data-bocchi-i18n="menu.custom.about-551516ab">关于</a>""");
        var values = ReadClientI18nValues(html, "menu.custom.about-551516ab");
        values.GetProperty("zh-CN").GetString().Should().Be("关于");
        values.GetProperty("en-US").GetString().Should().Be("About");
    }

    /// <summary>验证文章详情时间控件按更新时间配置选择默认态，同时保留点击切换需要的另一组时间事实。</summary>
    [Fact]
    public async Task RenderAsync_RendersArticleTimePreferenceAndTogglePayload()
    {
        var updatedHtml = await RenderSinglePostArticleAsync(showUpdatedAt: true, updatedAt: "2026-05-28T00:30:00Z");
        var updatedText = System.Net.WebUtility.HtmlDecode(updatedHtml);

        updatedHtml.Should().Contain("data-bocchi-article-time");
        updatedHtml.Should().Contain("data-bocchi-time=");
        updatedHtml.Should().Contain("data-bocchi-i18n=\"content.time.updatedAt\"");
        updatedHtml.Should().Contain("&quot;canToggle&quot;:true");
        updatedHtml.Should().Contain("&quot;offsetLabel&quot;");
        updatedHtml.Should().Contain("&quot;timeZoneLabel&quot;");
        updatedHtml.Should().Contain("Asia/Shanghai");
        updatedHtml.Should().NotContain("ASIA/SHANGHAI");
        updatedText.Should().Contain("修改于");
        updatedText.Should().Contain("2026-05-28 08:30");
        updatedText.Should().Contain("UTC+8");
        ReadClientI18nValues(updatedHtml, "content.time.authorTimeZone")
            .GetProperty("zh-CN")
            .GetString()
            .Should()
            .Be("作者时区");

        var writtenHtml = await RenderSinglePostArticleAsync(showUpdatedAt: false, updatedAt: "2026-05-28T00:30:00Z");
        var writtenText = System.Net.WebUtility.HtmlDecode(writtenHtml);
        writtenHtml.Should().Contain("data-bocchi-i18n=\"content.time.writtenAt\"");
        writtenHtml.Should().Contain("&quot;updated&quot;:{");
        writtenText.Should().Contain("编写于");
        writtenText.Should().Contain("2026-05-27 08:00");

        var sameInstantHtml = await RenderSinglePostArticleAsync(showUpdatedAt: true, updatedAt: "2026-05-27T08:00:00+08:00");
        var sameInstantText = System.Net.WebUtility.HtmlDecode(sameInstantHtml);
        sameInstantHtml.Should().Contain("&quot;canToggle&quot;:false");
        sameInstantText.Should().Contain("编写于");

        var missingUpdatedHtml = await RenderSinglePostArticleAsync(showUpdatedAt: true, updatedAt: null);
        var missingUpdatedText = System.Net.WebUtility.HtmlDecode(missingUpdatedHtml);
        missingUpdatedHtml.Should().Contain("&quot;canToggle&quot;:false");
        missingUpdatedText.Should().Contain("编写于");
        missingUpdatedHtml.Should().NotContain("&quot;updated&quot;:{");

        var ianaHtml = await RenderSinglePostArticleAsync(
            showUpdatedAt: true,
            updatedAt: "2026-05-28T00:30:00Z",
            timeZoneDisplayStyle: "ianaTimeZone");
        var ianaText = System.Net.WebUtility.HtmlDecode(ianaHtml);
        ianaHtml.Should().Contain("&quot;offsetLabel&quot;");
        ianaHtml.Should().Contain("&quot;timeZoneLabel&quot;:&quot;Asia/Shanghai&quot;");
        ianaHtml.Should().NotContain("ASIA/SHANGHAI");
        ianaText.Should().Contain("Asia/Shanghai");
    }

    /// <summary>验证 renderer 只消费 Theme input 给出的 URL/SEO/语言事实，并在写出阶段完成相对化和资产复制。</summary>
    [Fact]
    public async Task RenderAsync_ConsumesThemeInputFactsForUrlsSeoLanguageAndAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-default-static-" + Guid.NewGuid().ToString("N"));
        try
        {
            var themeRoot = Path.Combine(root, "theme");
            var inputDirectory = Path.Combine(root, "input");
            var outputDirectory = Path.Combine(root, "output");
            Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));
            Directory.CreateDirectory(inputDirectory);
            await File.WriteAllTextAsync(Path.Combine(themeRoot, "assets", "app.css"), "/* renderer custom css */");

            var postAlternates = CreateAlternates(
                ("posts/2025/direct@zh-CN", "zh-CN", "Direct Post", "/posts/2025/direct/", "https://renderer.example/posts/2025/direct/"),
                ("posts/2025/direct@zh-TW", "zh-TW", "繁中文章", "/zh-TW/posts/2025/direct/", "https://renderer.example/zh-TW/posts/2025/direct/"));
            var pageAlternates = CreateAlternates(
                ("pages/about@zh-CN", "zh-CN", "About", "/about/", "https://renderer.example/about/"),
                ("pages/about@en-US", "en-US", "About English", "/en-US/about/", "https://renderer.example/en-US/about/"));
            var workAlternates = CreateAlternates(
                ("works/2026/direct@zh-CN", "zh-CN", "Direct Work", "/works/2026/direct/", "https://renderer.example/works/2026/direct/"),
                ("works/2026/direct@zh-TW", "zh-TW", "繁中作品", "/zh-TW/works/2026/direct/", "https://renderer.example/zh-TW/works/2026/direct/"));

            await WriteEnvelopeAsync(inputDirectory, "theme-context.json", CreateThemeContext(includeEnglish: true));
            await WriteEnvelopeAsync(inputDirectory, "navigation.json", CreateNavigationWithEnglishAboutVariant());
            await WriteEnvelopeAsync(inputDirectory, "post-categories.json", Array.Empty<object>());
            await WriteEnvelopeAsync(inputDirectory, "posts.json", new[]
            {
                CreateContentVariant(
                    "posts/2025/direct@zh-CN",
                    "posts/2025/direct",
                    "zh-CN",
                    "Direct Post",
                    "/posts/2025/direct/",
                    "https://renderer.example/posts/2025/direct/",
                    "<p>Primary body.</p>",
                    postAlternates),
                CreateContentVariant(
                    "posts/2025/direct@zh-TW",
                    "posts/2025/direct",
                    "zh-TW",
                    "繁中文章",
                    "/zh-TW/posts/2025/direct/",
                    "https://renderer.example/zh-TW/posts/2025/direct/",
                    "<p>繁中正文 <a href=\"/about/\">About</a></p>",
                    postAlternates,
                    isTranslation: true,
                    sourceContentId: "posts/2025/direct@zh-CN",
                    sourceLanguage: "zh-CN",
                    updatedAt: "2026-05-28T00:30:00Z"),
            });
            await WriteEnvelopeAsync(inputDirectory, "pages.json", new[]
            {
                CreateContentVariant("pages/about@zh-CN", "pages/about", "zh-CN", "About", "/about/", "https://renderer.example/about/", "<p>About body.</p>", pageAlternates),
                CreateContentVariant("pages/about@en-US", "pages/about", "en-US", "About English", "/en-US/about/", "https://renderer.example/en-US/about/", "<p>English about body.</p>", pageAlternates, isTranslation: true, sourceContentId: "pages/about@zh-CN", sourceLanguage: "zh-CN"),
            });
            await WriteEnvelopeAsync(inputDirectory, "works.json", new[]
            {
                CreateContentVariant("works/2026/direct@zh-CN", "works/2026/direct", "zh-CN", "Direct Work", "/works/2026/direct/", "https://renderer.example/works/2026/direct/", "<p>Work body.</p>", workAlternates),
                CreateContentVariant("works/2026/direct@zh-TW", "works/2026/direct", "zh-TW", "繁中作品", "/zh-TW/works/2026/direct/", "https://renderer.example/zh-TW/works/2026/direct/", "<p>作品正文。</p>", workAlternates, isTranslation: true, sourceContentId: "works/2026/direct@zh-CN", sourceLanguage: "zh-CN"),
            });
            await WriteEnvelopeAsync(inputDirectory, "notes.json", Array.Empty<object>());
            await WriteEnvelopeAsync(inputDirectory, "friends.json", Array.Empty<object>());

            await DefaultStaticTemplateRenderer.RenderAsync(
                new DefaultStaticRenderRequest
                {
                    ThemeRoot = themeRoot,
                    InputDirectory = inputDirectory,
                    OutputDirectory = outputDirectory,
                    Manifest = CreateManifest(),
                    BaseUrl = "https://renderer.example/",
                    Environment = "production",
                });

            (await File.ReadAllTextAsync(Path.Combine(outputDirectory, "assets", "app.css")))
                .Should().Be("/* renderer custom css */");
            var appJs = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "assets", "app.js"));
            appJs.Should().Contain("data-bocchi-page-hrefs");
            appJs.Should().Contain("bocchi:languagechange");

            var indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));
            indexHtml.Should().Contain("href=\"assets/app.css\"");
            indexHtml.Should().Contain("src=\"assets/app.js\"");
            indexHtml.Should().Contain("href=\"posts/\"");
            indexHtml.Should().NotContain("""href="/""");
            indexHtml.Should().NotContain("""src="/""");
            var indexText = System.Net.WebUtility.HtmlDecode(indexHtml);
            indexText.Should().Contain("Direct Post");
            indexText.Should().Contain("Direct Work");
            indexHtml.Should().Contain("""<h3><a href="works/2026/direct/" aria-label="Direct Work" data-bocchi-content-link><span data-bocchi-content-title>Direct Work</span></a></h3>""");
            indexHtml.Should().NotContain("proj-card__link");
            indexHtml.Should().Contain("data-bocchi-content-link");
            indexHtml.Should().Contain("data-bocchi-content-variants=");
            indexHtml.Should().Contain("zh-TW/posts/2025/direct/");
            indexHtml.Should().Contain("zh-TW/works/2026/direct/");
            indexText.Should().Contain("data-bocchi-nav-hrefs=");

            var postListHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "posts", "index.html"));
            var postListText = System.Net.WebUtility.HtmlDecode(postListHtml);
            postListText.Should().Contain("Direct Post");
            postListHtml.Should().Contain("zh-TW/posts/2025/direct/");
            CountOccurrences(postListHtml, "data-bocchi-content-variants=").Should().Be(1);
            var workListHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "works", "index.html"));
            var workListText = System.Net.WebUtility.HtmlDecode(workListHtml);
            workListText.Should().Contain("Direct Work");
            workListHtml.Should().Contain("zh-TW/works/2026/direct/");

            var postHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "zh-TW", "posts", "2025", "direct", "index.html"));
            postHtml.Should().Contain("""<html lang="zh-TW">""");
            postHtml.Should().Contain("""<link rel="canonical" href="https://renderer.example/zh-TW/posts/2025/direct/">""");
            postHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://renderer.example/posts/2025/direct/">""");
            postHtml.Should().Contain("""<link rel="alternate" hreflang="zh-TW" href="https://renderer.example/zh-TW/posts/2025/direct/">""");
            postHtml.Should().Contain("""data-bocchi-article-time""");
            postHtml.Should().Contain("""data-bocchi-page-hrefs=""");
            postHtml.Should().NotContain("""data-bocchi-language-switch""");
            postHtml.Should().Contain("""data-bocchi-translation-notice""");
            postHtml.Should().Contain("data-bocchi-i18n=\"content.viewOriginal\"");
            postHtml.Should().Contain("data-bocchi-language-link=\"zh-CN\"");
            postHtml.Should().Contain("href=\"../../../../posts/2025/direct/\"");
            postHtml.Should().Contain("href=\"../../../../about/\"");
            postHtml.Should().Contain("href=\"../../../../assets/app.css\"");
            postHtml.Should().NotContain("""href="/""");
            postHtml.Should().NotContain("""src="/""");

            var primaryPageHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "about", "index.html"));
            primaryPageHtml.Should().Contain("""data-bocchi-page-hrefs=""");
            primaryPageHtml.Should().Contain("""&quot;en-US&quot;:&quot;../en-US/about/&quot;""");
            primaryPageHtml.Should().Contain("""data-bocchi-nav-hrefs=""");
            primaryPageHtml.Should().Contain("""&quot;en-US&quot;:&quot;../en-US/about/&quot;""");

            var pageHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "en-US", "about", "index.html"));
            pageHtml.Should().Contain("""<html lang="en-US">""");
            pageHtml.Should().Contain("""<link rel="canonical" href="https://renderer.example/en-US/about/">""");
            pageHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://renderer.example/about/">""");
            pageHtml.Should().Contain("""data-bocchi-page-hrefs=""");
            pageHtml.Should().Contain("""&quot;zh-CN&quot;:&quot;../../about/&quot;""");
            pageHtml.Should().Contain("""&quot;en-US&quot;:&quot;./&quot;""");
            pageHtml.Should().Contain("href=\"./\" class=\"active\" aria-current=\"page\" data-bocchi-i18n=\"menu.custom.about-551516ab\"");
            pageHtml.Should().NotContain("""data-bocchi-language-switch""");
            pageHtml.Should().Contain("""data-bocchi-translation-notice""");
            pageHtml.Should().Contain("data-bocchi-language-link=\"zh-CN\"");

            var workHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "zh-TW", "works", "2026", "direct", "index.html"));
            workHtml.Should().Contain("""<html lang="zh-TW">""");
            workHtml.Should().Contain("""<link rel="canonical" href="https://renderer.example/zh-TW/works/2026/direct/">""");
            workHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://renderer.example/works/2026/direct/">""");
            workHtml.Should().Contain("""data-bocchi-page-hrefs=""");
            workHtml.Should().NotContain("""data-bocchi-language-switch""");
            workHtml.Should().Contain("""data-bocchi-translation-notice""");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>写入测试用 Theme Contract envelope，保持与 Generator 输出的外层结构一致。</summary>
    private static Task WriteEnvelopeAsync(string inputDirectory, string fileName, object data)
    {
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["$schema"] = $"https://bocchi.local/schema/v1/{fileName}",
            ["contractVersion"] = ThemeContractVersion.Current,
            ["generatedAt"] = "2026-05-27T00:00:00Z",
            ["data"] = data,
        };
        return File.WriteAllTextAsync(Path.Combine(inputDirectory, fileName), JsonSerializer.Serialize(envelope, JsonOptions));
    }

    /// <summary>从页面内嵌客户端 i18n JSON 读取指定 key 的语言表，避免测试依赖 Unicode 转义形态。</summary>
    private static JsonElement ReadClientI18nValues(string html, string key)
    {
        const string marker = """id="bocchi-i18n-data">""";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += marker.Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        using var document = JsonDocument.Parse(html[start..end]);
        return document.RootElement
            .GetProperty("text")
            .GetProperty(key)
            .Clone();
    }

    /// <summary>创建 renderer 请求需要的最小 manifest；当前 renderer 不从这里派生 URL 或 SEO 信息。</summary>
    private static ThemeManifest CreateManifest()
        => new()
        {
            Id = "default-static",
            Name = "Default Static",
            Version = "0.1.0",
            ContractVersion = ThemeContractVersion.Current,
        };

    /// <summary>创建默认 Theme 渲染所需的最小 theme-context 数据。</summary>
    private static Dictionary<string, object?> CreateThemeContext(
        IReadOnlyDictionary<string, object?>? localizationText = null,
        IReadOnlyList<object>? themeI18nKeys = null,
        IReadOnlyDictionary<string, string>? heroTitle = null,
        IReadOnlyDictionary<string, string>? heroSubtitle = null,
        bool includeEnglish = false,
        bool showUpdatedAt = true,
        string timeZoneDisplayStyle = "utcOffset")
        => new(StringComparer.Ordinal)
        {
            ["site"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = "Renderer Site",
                ["defaultTitle"] = "Renderer Site",
                ["description"] = "Renderer focused test site",
                ["language"] = "zh-CN",
                ["baseUrl"] = "https://renderer.example/",
                ["copyrightNotice"] = "Copyright 2026 Renderer Site.",
                ["timeZone"] = "Asia/Shanghai",
            },
            ["author"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["displayName"] = "Renderer Author",
                ["timeZone"] = "Asia/Shanghai",
            },
            ["build"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["mode"] = "full",
                ["generatedAt"] = "2026-05-27T00:00:00Z",
                ["includeDrafts"] = false,
            },
            ["theme"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = "default-static",
                ["config"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["visual"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["accentColor"] = "#E85D3A",
                    },
                    ["reading"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["showUpdatedAt"] = showUpdatedAt,
                        ["timeZoneDisplayStyle"] = timeZoneDisplayStyle,
                    },
                    ["home"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["featuredPosts"] = 2,
                        ["featuredWorks"] = 2,
                        ["recentNotes"] = 2,
                        ["heroTitle"] = heroTitle ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["zh-CN"] = "Renderer Home",
                        },
                        ["heroSubtitle"] = heroSubtitle ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["zh-CN"] = "Renderer Subtitle",
                        },
                        ["tags"] = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["zh-CN"] = ["Renderer"],
                        },
                    },
                },
                ["i18n"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["defaultLanguage"] = "en-US",
                    ["keys"] = themeI18nKeys ?? Array.Empty<object>(),
                },
            },
            ["localization"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["primaryLanguage"] = "zh-CN",
                ["urlPolicy"] = "PrimaryUnprefixed",
                ["enabledLanguages"] = CreateEnabledLanguages(includeEnglish),
                ["text"] = localizationText ?? new Dictionary<string, object?>(StringComparer.Ordinal),
            },
        };

    /// <summary>创建测试语言列表；需要覆盖浏览器切换英文时才加入 en-US。</summary>
    private static Dictionary<string, object?>[] CreateEnabledLanguages(bool includeEnglish)
    {
        var languages = new List<Dictionary<string, object?>>
        {
            new(StringComparer.Ordinal)
            {
                ["code"] = "zh-CN",
                ["nativeName"] = "简体中文",
                ["englishName"] = "Simplified Chinese",
            },
        };
        if (includeEnglish)
        {
            languages.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["code"] = "en-US",
                ["nativeName"] = "English",
                ["englishName"] = "English",
            });
        }

        languages.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = "zh-TW",
            ["nativeName"] = "繁體中文",
            ["englishName"] = "Traditional Chinese",
        });
        return languages.ToArray();
    }

    /// <summary>执行一次最小首页渲染，方便 focused tests 验证默认 Theme 文案模型。</summary>
    private static async Task<string> RenderIndexAsync(Dictionary<string, object?> themeContext, object? navigation = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-default-static-" + Guid.NewGuid().ToString("N"));
        try
        {
            var themeRoot = Path.Combine(root, "theme");
            var inputDirectory = Path.Combine(root, "input");
            var outputDirectory = Path.Combine(root, "output");
            Directory.CreateDirectory(themeRoot);
            Directory.CreateDirectory(inputDirectory);

            await WriteMinimalInputAsync(inputDirectory, themeContext, navigation ?? CreateNavigation());
            await DefaultStaticTemplateRenderer.RenderAsync(
                new DefaultStaticRenderRequest
                {
                    ThemeRoot = themeRoot,
                    InputDirectory = inputDirectory,
                    OutputDirectory = outputDirectory,
                    Manifest = CreateManifest(),
                    BaseUrl = "https://renderer.example/",
                    Environment = "production",
                });

            return await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>渲染单篇文章详情页，专门保护文章 caption 中的交互式时间控件。</summary>
    private static async Task<string> RenderSinglePostArticleAsync(
        bool showUpdatedAt,
        string? updatedAt,
        string timeZoneDisplayStyle = "utcOffset")
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-default-static-" + Guid.NewGuid().ToString("N"));
        try
        {
            var themeRoot = Path.Combine(root, "theme");
            var inputDirectory = Path.Combine(root, "input");
            var outputDirectory = Path.Combine(root, "output");
            Directory.CreateDirectory(themeRoot);
            Directory.CreateDirectory(inputDirectory);

            var alternates = new[]
            {
                CreateAlternate(("posts/2026/time@zh-CN", "zh-CN", "时间文章", "/posts/2026/time/", "https://renderer.example/posts/2026/time/")),
            };
            await WriteEnvelopeAsync(
                inputDirectory,
                "theme-context.json",
                CreateThemeContext(showUpdatedAt: showUpdatedAt, timeZoneDisplayStyle: timeZoneDisplayStyle));
            await WriteEnvelopeAsync(inputDirectory, "navigation.json", CreateNavigation());
            await WriteEnvelopeAsync(inputDirectory, "post-categories.json", Array.Empty<object>());
            await WriteEnvelopeAsync(inputDirectory, "posts.json", new[]
            {
                CreateContentVariant(
                    "posts/2026/time@zh-CN",
                    "posts/2026/time",
                    "zh-CN",
                    "时间文章",
                    "/posts/2026/time/",
                    "https://renderer.example/posts/2026/time/",
                    "<p>正文。</p>",
                    alternates,
                    updatedAt: updatedAt),
            });
            await WriteEnvelopeAsync(inputDirectory, "pages.json", Array.Empty<object>());
            await WriteEnvelopeAsync(inputDirectory, "works.json", Array.Empty<object>());
            await WriteEnvelopeAsync(inputDirectory, "notes.json", Array.Empty<object>());
            await WriteEnvelopeAsync(inputDirectory, "friends.json", Array.Empty<object>());

            await DefaultStaticTemplateRenderer.RenderAsync(
                new DefaultStaticRenderRequest
                {
                    ThemeRoot = themeRoot,
                    InputDirectory = inputDirectory,
                    OutputDirectory = outputDirectory,
                    Manifest = CreateManifest(),
                    BaseUrl = "https://renderer.example/",
                    Environment = "production",
                });

            return await File.ReadAllTextAsync(Path.Combine(outputDirectory, "posts", "2026", "time", "index.html"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>写入渲染首页所需的最小 Theme Contract 输入集合。</summary>
    private static async Task WriteMinimalInputAsync(string inputDirectory, Dictionary<string, object?> themeContext, object navigation)
    {
        await WriteEnvelopeAsync(inputDirectory, "theme-context.json", themeContext);
        await WriteEnvelopeAsync(inputDirectory, "navigation.json", navigation);
        await WriteEnvelopeAsync(inputDirectory, "post-categories.json", Array.Empty<object>());
        await WriteEnvelopeAsync(inputDirectory, "posts.json", Array.Empty<object>());
        await WriteEnvelopeAsync(inputDirectory, "pages.json", Array.Empty<object>());
        await WriteEnvelopeAsync(inputDirectory, "works.json", Array.Empty<object>());
        await WriteEnvelopeAsync(inputDirectory, "notes.json", Array.Empty<object>());
        await WriteEnvelopeAsync(inputDirectory, "friends.json", Array.Empty<object>());
    }

    /// <summary>创建一个 Theme manifest i18n key 默认值声明。</summary>
    private static Dictionary<string, object?> CreateThemeI18nKey(
        string key,
        params (string Language, string Value)[] values)
        => new(StringComparer.Ordinal)
        {
            ["key"] = key,
            ["title"] = key,
            ["defaultValues"] = CreateLanguageText(values),
        };

    /// <summary>创建 language -> text 的测试用 plain text 文案对象。</summary>
    private static Dictionary<string, string> CreateLanguageText(params (string Language, string Value)[] values)
        => values.ToDictionary(value => value.Language, value => value.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>创建测试导航，供相对 URL 改写覆盖首页与列表链接。</summary>
    private static Dictionary<string, object?> CreateNavigation()
        => new(StringComparer.Ordinal)
        {
            ["items"] = new[]
            {
                CreateNavigationItem("home", "Home", "/"),
                CreateNavigationItem("posts", "Posts", "/posts/"),
                CreateNavigationItem(
                    "about",
                    "About",
                    "/about/",
                    languageHrefs: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["zh-CN"] = "/about/",
                        ["zh-TW"] = "/zh-TW/about/",
                    }),
            },
        };

    /// <summary>创建 About 拥有 en-US variant 的导航，覆盖前台语言上下文切换真实用户场景。</summary>
    private static Dictionary<string, object?> CreateNavigationWithEnglishAboutVariant()
        => new(StringComparer.Ordinal)
        {
            ["items"] = new[]
            {
                CreateNavigationItem("home", "Home", "/"),
                CreateNavigationItem("posts", "Posts", "/posts/"),
                CreateNavigationItem(
                    "about",
                    "About",
                    "/about/",
                    "menu.custom.about-551516ab",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["zh-CN"] = "/about/",
                        ["en-US"] = "/en-US/about/",
                    }),
            },
        };

    /// <summary>创建带自定义 Common i18n 文案的测试导航，模拟 Dashboard 生成的 menu.custom.* key。</summary>
    private static Dictionary<string, object?> CreateNavigationWithCustomI18nLabel()
        => new(StringComparer.Ordinal)
        {
            ["items"] = new[]
            {
                CreateNavigationItem("about", "关于", "/about/", "menu.custom.about-551516ab"),
            },
        };

    /// <summary>创建单个导航节点。</summary>
    private static Dictionary<string, object?> CreateNavigationItem(
        string id,
        string label,
        string href,
        string? commonI18nKey = null,
        IReadOnlyDictionary<string, string>? languageHrefs = null)
        => new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["label"] = label,
            ["href"] = href,
            ["languageHrefs"] = languageHrefs,
            ["labelI18n"] = string.IsNullOrWhiteSpace(commonI18nKey)
                ? null
                : new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scope"] = "common",
                    ["key"] = commonI18nKey,
                    ["raw"] = "i18n://common@" + commonI18nKey,
                },
            ["children"] = Array.Empty<object>(),
        };

    /// <summary>统计静态 HTML 片段出现次数，避免 data attribute 中的备用语言标题干扰可见列表断言。</summary>
    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>创建一组主语言与翻译 variant 的 alternate 事实。</summary>
    private static Dictionary<string, object?>[] CreateAlternates(
        (string ContentId, string Language, string Title, string SiteRelativeUrl, string Href) primary,
        (string ContentId, string Language, string Title, string SiteRelativeUrl, string Href) translation)
        => [CreateAlternate(primary), CreateAlternate(translation)];

    /// <summary>创建单个 alternate 事实，模拟 T06 已输出的稳定 URL 字段。</summary>
    private static Dictionary<string, object?> CreateAlternate(
        (string ContentId, string Language, string Title, string SiteRelativeUrl, string Href) alternate)
        => new(StringComparer.Ordinal)
        {
            ["contentId"] = alternate.ContentId,
            ["language"] = alternate.Language,
            ["title"] = alternate.Title,
            ["siteRelativeUrl"] = alternate.SiteRelativeUrl,
            ["url"] = alternate.SiteRelativeUrl,
            ["href"] = alternate.Href,
            ["hreflang"] = alternate.Language,
        };

    /// <summary>创建 Post/Page/Work 共用的内容 variant 输入。</summary>
    private static Dictionary<string, object?> CreateContentVariant(
        string id,
        string groupId,
        string language,
        string title,
        string siteRelativeUrl,
        string canonicalUrl,
        string html,
        Dictionary<string, object?>[] alternates,
        bool isTranslation = false,
        string sourceContentId = "",
        string sourceLanguage = "",
        string publishedAt = "2026-05-27T00:00:00Z",
        string? updatedAt = null)
        => new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["title"] = title,
            ["summary"] = title + " summary",
            ["slug"] = siteRelativeUrl.Trim('/').Split('/').Last(),
            ["status"] = "published",
            ["language"] = language,
            ["siteRelativeUrl"] = siteRelativeUrl,
            ["url"] = siteRelativeUrl,
            ["canonicalUrl"] = canonicalUrl,
            ["publishedAt"] = publishedAt,
            ["updatedAt"] = updatedAt,
            ["html"] = html,
            ["tags"] = Array.Empty<string>(),
            ["stack"] = Array.Empty<string>(),
            ["links"] = Array.Empty<object>(),
            ["localization"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["groupId"] = groupId,
                ["isTranslation"] = isTranslation,
                ["sourceContentId"] = sourceContentId,
                ["sourceLanguage"] = sourceLanguage,
                ["alternates"] = alternates,
            },
        };
}
