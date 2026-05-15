using System.Text;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme 的工作区物化定义。</summary>
public sealed class DefaultStaticThemeDefinition
{
    /// <summary>默认 Theme id。该值同时出现在 site.yaml、theme.json 和 Dashboard 配置中。</summary>
    public const string ThemeId = "default-static";

    /// <summary>默认 Theme 显示名称。</summary>
    public const string ThemeName = "Bocchi Mono";

    /// <summary>默认 Theme 初始版本。</summary>
    public const string ThemeVersion = "0.1.0";

    /// <summary>把默认 Theme 的可见运行实例物化到 workspace themes 目录。已存在文件不会被覆盖。</summary>
    public static async Task EnsureAsync(string themesDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themesDirectory);
        var themeRoot = Path.Combine(themesDirectory, ThemeId);
        Directory.CreateDirectory(themeRoot);
        Directory.CreateDirectory(Path.Combine(themeRoot, "templates", "layouts"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "templates", "pages"));
        Directory.CreateDirectory(Path.Combine(themeRoot, "assets"));

        await EnsureFileAsync(Path.Combine(themeRoot, "theme.json"), ThemeJson, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "config-schema.json"), ConfigSchemaJson, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "layouts", "base.liquid"), BaseTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "index.liquid"), IndexTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "favicon.svg"), DefaultStaticThemeAssets.FaviconSvg, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "app.css"), DefaultStaticThemeAssets.Css, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "app.js"), DefaultStaticThemeAssets.Js, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>只在目标文件不存在时写入默认内容，避免覆盖用户修改后的 Theme 文件。</summary>
    private static async Task EnsureFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return;
        }

        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>默认 Theme manifest。runner 入口保持为 fluid，后续接入真实 Fluid 模板语法时无需迁移工作区 manifest。</summary>
    private const string ThemeJson = """
        {
          "id": "default-static",
          "name": "Bocchi Mono",
          "version": "0.1.0",
          "contractVersion": "1.0",
          "inputDir": ".bocchi/input",
          "outputDir": "build",
          "runner": {
            "kind": "builtin-template",
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
        """;

    /// <summary>Dashboard 可读取的默认 Theme 配置 schema。</summary>
    private const string ConfigSchemaJson = """
        {
          "schemaVersion": "1.0",
          "groups": [
            {
              "id": "visual",
              "title": "视觉",
              "fields": [
                {
                  "key": "visual.accentColor",
                  "type": "color",
                  "title": "Accent color",
                  "default": "#E85D3A"
                }
              ]
            },
            {
              "id": "home",
              "title": "首页",
              "fields": [
                {
                  "key": "home.featuredPosts",
                  "type": "number",
                  "title": "首页文章数量",
                  "default": 5
                },
                {
                  "key": "home.featuredWorks",
                  "type": "number",
                  "title": "首页作品数量",
                  "default": 4
                },
                {
                  "key": "home.recentNotes",
                  "type": "number",
                  "title": "首页短文数量",
                  "default": 3
                },
                {
                  "key": "home.showFriends",
                  "type": "boolean",
                  "title": "首页显示友链",
                  "default": true
                }
              ]
            },
            {
              "id": "reading",
              "title": "阅读",
              "fields": [
                {
                  "key": "reading.showUpdatedAt",
                  "type": "boolean",
                  "title": "显示更新时间",
                  "default": true
                }
              ]
            }
          ]
        }
        """;

    /// <summary>给用户看的布局模板占位；当前 renderer 已先用 typed helper 输出页面。</summary>
    private const string BaseTemplate = """
        <!doctype html>
        <html lang="{{ site.language }}">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{ page.title }} · {{ site.title }}</title>
          <link rel="icon" type="image/svg+xml" href="/assets/favicon.svg">
          <link rel="stylesheet" href="/assets/app.css">
        </head>
        <body>
          {{ content | html }}
          <script type="module" src="/assets/app.js"></script>
        </body>
        </html>
        """;

    /// <summary>给用户看的首页模板占位；M5 后续会把 typed 输出逐步迁入 Fluid 模板。</summary>
    private const string IndexTemplate = """
        <main class="site-main">
          <section class="hero">
            <p class="eyebrow">Index</p>
            <h1>{{ site.title }}</h1>
            <p>{{ site.description }}</p>
          </section>
        </main>
        """;
}
