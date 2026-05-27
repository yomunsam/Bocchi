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
                ("pages/about@zh-TW", "zh-TW", "關於", "/zh-TW/about/", "https://renderer.example/zh-TW/about/"));
            var workAlternates = CreateAlternates(
                ("works/2026/direct@zh-CN", "zh-CN", "Direct Work", "/works/2026/direct/", "https://renderer.example/works/2026/direct/"),
                ("works/2026/direct@zh-TW", "zh-TW", "繁中作品", "/zh-TW/works/2026/direct/", "https://renderer.example/zh-TW/works/2026/direct/"));

            await WriteEnvelopeAsync(inputDirectory, "theme-context.json", CreateThemeContext());
            await WriteEnvelopeAsync(inputDirectory, "navigation.json", CreateNavigation());
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
                    sourceLanguage: "zh-CN"),
            });
            await WriteEnvelopeAsync(inputDirectory, "pages.json", new[]
            {
                CreateContentVariant("pages/about@zh-CN", "pages/about", "zh-CN", "About", "/about/", "https://renderer.example/about/", "<p>About body.</p>", pageAlternates),
                CreateContentVariant("pages/about@zh-TW", "pages/about", "zh-TW", "關於", "/zh-TW/about/", "https://renderer.example/zh-TW/about/", "<p>關於正文。</p>", pageAlternates, isTranslation: true, sourceContentId: "pages/about@zh-CN", sourceLanguage: "zh-CN"),
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

            var indexHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "index.html"));
            indexHtml.Should().Contain("href=\"assets/app.css\"");
            indexHtml.Should().Contain("src=\"assets/app.js\"");
            indexHtml.Should().Contain("href=\"posts/\"");
            indexHtml.Should().NotContain("""href="/""");
            indexHtml.Should().NotContain("""src="/""");

            var postHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "zh-TW", "posts", "2025", "direct", "index.html"));
            postHtml.Should().Contain("""<html lang="zh-TW">""");
            postHtml.Should().Contain("""<link rel="canonical" href="https://renderer.example/zh-TW/posts/2025/direct/">""");
            postHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://renderer.example/posts/2025/direct/">""");
            postHtml.Should().Contain("""<link rel="alternate" hreflang="zh-TW" href="https://renderer.example/zh-TW/posts/2025/direct/">""");
            postHtml.Should().Contain("""data-bocchi-language-switch""");
            postHtml.Should().Contain("""data-bocchi-translation-notice""");
            postHtml.Should().Contain("data-bocchi-i18n=\"content.viewOriginal\"");
            postHtml.Should().Contain("href=\"../../../../posts/2025/direct/\"");
            postHtml.Should().Contain("href=\"../../../../about/\"");
            postHtml.Should().Contain("href=\"../../../../assets/app.css\"");
            postHtml.Should().NotContain("""href="/""");
            postHtml.Should().NotContain("""src="/""");

            var pageHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "zh-TW", "about", "index.html"));
            pageHtml.Should().Contain("""<html lang="zh-TW">""");
            pageHtml.Should().Contain("""<link rel="canonical" href="https://renderer.example/zh-TW/about/">""");
            pageHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://renderer.example/about/">""");
            pageHtml.Should().Contain("""data-bocchi-language-switch""");
            pageHtml.Should().Contain("""data-bocchi-translation-notice""");

            var workHtml = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "zh-TW", "works", "2026", "direct", "index.html"));
            workHtml.Should().Contain("""<html lang="zh-TW">""");
            workHtml.Should().Contain("""<link rel="canonical" href="https://renderer.example/zh-TW/works/2026/direct/">""");
            workHtml.Should().Contain("""<link rel="alternate" hreflang="zh-CN" href="https://renderer.example/works/2026/direct/">""");
            workHtml.Should().Contain("""data-bocchi-language-switch""");
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
    private static Dictionary<string, object?> CreateThemeContext()
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
                    ["home"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["featuredPosts"] = 2,
                        ["featuredWorks"] = 2,
                        ["recentNotes"] = 2,
                        ["heroTitle"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["zh-CN"] = "Renderer Home",
                        },
                        ["heroSubtitle"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
                    ["keys"] = Array.Empty<object>(),
                },
            },
            ["localization"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["primaryLanguage"] = "zh-CN",
                ["urlPolicy"] = "PrimaryUnprefixed",
                ["enabledLanguages"] = new[]
                {
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["code"] = "zh-CN",
                        ["nativeName"] = "简体中文",
                        ["englishName"] = "Simplified Chinese",
                    },
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["code"] = "zh-TW",
                        ["nativeName"] = "繁體中文",
                        ["englishName"] = "Traditional Chinese",
                    },
                },
                ["text"] = new Dictionary<string, object?>(StringComparer.Ordinal),
            },
        };

    /// <summary>创建测试导航，供相对 URL 改写覆盖首页与列表链接。</summary>
    private static Dictionary<string, object?> CreateNavigation()
        => new(StringComparer.Ordinal)
        {
            ["items"] = new[]
            {
                CreateNavigationItem("home", "Home", "/"),
                CreateNavigationItem("posts", "Posts", "/posts/"),
                CreateNavigationItem("about", "About", "/about/"),
            },
        };

    /// <summary>创建单个导航节点。</summary>
    private static Dictionary<string, object?> CreateNavigationItem(string id, string label, string href)
        => new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["label"] = label,
            ["href"] = href,
            ["children"] = Array.Empty<object>(),
        };

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
        string sourceLanguage = "")
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
            ["publishedAt"] = "2026-05-27T00:00:00Z",
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
