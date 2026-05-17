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
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "posts.liquid"), PostsTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "works.liquid"), WorksTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "notes.liquid"), NotesTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "friends.liquid"), FriendsTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "article.liquid"), ArticleTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "standalone-page.liquid"), StandalonePageTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "404.liquid"), NotFoundTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "favicon.svg"), DefaultStaticThemeAssets.FaviconSvg, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "app.css"), DefaultStaticThemeAssets.Css, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "app.js"), DefaultStaticThemeAssets.Js, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取内置默认模板，供旧工作区缺少新增模板文件时回退。</summary>
    internal static string? TryGetTemplate(string relativePath)
        => relativePath.Replace('\\', '/') switch
        {
            "layouts/base.liquid" => BaseTemplate,
            "pages/index.liquid" => IndexTemplate,
            "pages/posts.liquid" => PostsTemplate,
            "pages/works.liquid" => WorksTemplate,
            "pages/notes.liquid" => NotesTemplate,
            "pages/friends.liquid" => FriendsTemplate,
            "pages/article.liquid" => ArticleTemplate,
            "pages/standalone-page.liquid" => StandalonePageTemplate,
            "pages/404.liquid" => NotFoundTemplate,
            _ => null,
        };

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
          "inputDir": "../../cache/theme-input",
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
          },
          "i18n": {
            "supportedLanguages": ["en-US", "zh-CN", "zh-TW", "ja-JP"],
            "defaultLanguage": "en-US",
            "keys": [
              {
                "key": "theme.defaultStatic.colophonBuiltWith",
                "title": "Colophon built-with text",
                "description": "Footer text used by Bocchi Mono.",
                "defaultValues": {
                  "en-US": "Built with Bocchi.",
                  "zh-CN": "由 Bocchi 构建。",
                  "zh-TW": "由 Bocchi 構建。",
                  "ja-JP": "Bocchi で構築。"
                }
              },
              {
                "key": "theme.defaultStatic.emptyList",
                "title": "Empty list text",
                "description": "Message shown when a content section has no published items.",
                "defaultValues": {
                  "en-US": "Nothing here yet.",
                  "zh-CN": "这里还没有内容。",
                  "zh-TW": "這裡還沒有內容。",
                  "ja-JP": "まだ何もありません。"
                }
              }
            ]
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

    /// <summary>默认全局布局模板。</summary>
    private const string BaseTemplate = """
        <!doctype html>
        <html lang="{{ site.language }}">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="description" content="{{ site.description }}">
          <title>{{ page.fullTitle }}</title>
          <link rel="icon" type="image/svg+xml" href="/assets/favicon.svg">
          <link rel="stylesheet" href="/assets/app.css">
          <style>:root{--accent: {{ site.accentColor }};}</style>
        </head>
        <body>
          <header class="topbar">
            <div class="topbar__inner">
              <a class="wordmark" href="/">{{ site.title }}</a>
              <nav class="nav" aria-label="Primary">
                {% for item in navigation %}<a href="{{ item.href }}"{% if item.current %} aria-current="page"{% endif %}>{{ item.label }}</a>{% endfor %}
              </nav>
              <div class="toolbar">
                <button class="icon-button" type="button" data-theme-toggle aria-label="Toggle appearance">◐</button>
                <button class="icon-button mobile-toggle" type="button" data-mobile-toggle aria-expanded="false" aria-label="Open menu">☰</button>
              </div>
            </div>
            <nav class="mobile-nav" data-mobile-nav aria-label="Mobile primary">
              {% for item in navigation %}<a href="{{ item.href }}"{% if item.current %} aria-current="page"{% endif %}>{{ item.label }}</a>{% endfor %}
            </nav>
          </header>
          <main>{{ content | html }}</main>
          <footer class="footer">
            <div class="footer__inner">
              <span>{{ site.title }} · {{ site.generatedYear }}</span>
              <span><a href="/feed.xml">RSS</a> · <a href="/sitemap.xml">Sitemap</a></span>
            </div>
          </footer>
          <script type="module" src="/assets/app.js"></script>
        </body>
        </html>
        """;

    /// <summary>默认首页模板。</summary>
    private const string IndexTemplate = """
        <section class="hero container">
          <p class="eyebrow">Index · {{ site.authorTimeZone }}</p>
          <h1>{{ site.title }} <em>writing</em>, work, and notes.</h1>
          <p class="lead">{{ site.description }}</p>
          <div class="meta-row"><span>{{ site.authorName }}</span><span>{{ site.language }}</span><span>{{ site.baseUrl }}</span></div>
        </section>
        <section class="content section">
          <div class="section-head"><h2>Selected Writing</h2><a class="arrow-link" href="/posts/">All</a></div>
          {% if hasFeaturedPosts %}
          <div class="list">
            {% for item in featuredPosts %}<a class="list-row" href="{{ item.url }}"><span class="list-row__date">{{ item.yearMonth }}</span><span class="list-row__title">{{ item.title }}</span><span class="list-row__meta">{{ item.meta }}</span></a>{% endfor %}
          </div>
          {% else %}<div class="empty">No writing yet.</div>{% endif %}
        </section>
        <section class="content section">
          <div class="section-head"><h2>Selected Work</h2><a class="arrow-link" href="/works/">All</a></div>
          {% if hasFeaturedWorks %}
          <div class="grid">
            {% for item in featuredWorks %}
            <article class="card">
              {% if item.hasCover %}<a class="card__cover" href="{{ item.url }}"><img src="{{ item.cover.path }}" alt="{{ item.cover.alt }}"></a>{% endif %}
              <h3><a href="{{ item.url }}">{{ item.title }}</a></h3>
              <p>{{ item.summary }}</p>
              {% if item.hasStack %}<div class="tags">{% for tag in item.stack %}<span>{{ tag }}</span>{% endfor %}</div>{% endif %}
            </article>
            {% endfor %}
          </div>
          {% else %}<div class="empty">No work entries yet.</div>{% endif %}
        </section>
        <section class="content section">
          <div class="section-head"><h2>Recent Notes</h2><a class="arrow-link" href="/notes/">All</a></div>
          {% if hasRecentNotes %}
            {% for item in recentNotes %}<article class="note"><bocchi-time datetime="{{ item.isoDate }}" author-time-zone="{{ site.authorTimeZone }}"><time>{{ item.displayDateTime }}</time></bocchi-time><div class="note__body">{{ item.html | html }}</div></article>{% endfor %}
          {% else %}<div class="empty">No notes yet.</div>{% endif %}
        </section>
        {% if showFriends %}
        <section class="content section">
          <div class="section-head"><h2>Friends</h2><a class="arrow-link" href="/friends/">All</a></div>
          {% if hasFriends %}
          <div class="list">
            {% for item in friends %}<a class="list-row" href="{{ item.url }}"><span class="list-row__date">Link</span><span class="list-row__title">{{ item.title }}</span><span class="list-row__meta">{{ item.summary }}</span></a>{% endfor %}
          </div>
          {% else %}<div class="empty">No friend links yet.</div>{% endif %}
        </section>
        {% endif %}
        """;

    /// <summary>默认文章列表模板。</summary>
    private const string PostsTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1>{{ hero.title }}</h1>
          <p class="lead">{{ hero.description }}</p>
        </section>
        <section class="content section">
          {% if hasItems %}
          <div class="list">
            {% for item in items %}<a class="list-row" href="{{ item.url }}"><span class="list-row__date">{{ item.yearMonth }}</span><span class="list-row__title">{{ item.title }}</span><span class="list-row__meta">{{ item.meta }}</span></a>{% endfor %}
          </div>
          {% else %}<div class="empty">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认作品列表模板。</summary>
    private const string WorksTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1>{{ hero.title }}</h1>
          <p class="lead">{{ hero.description }}</p>
        </section>
        <section class="content section">
          {% if hasItems %}
          <div class="grid">
            {% for item in items %}
            <article class="card">
              {% if item.hasCover %}<a class="card__cover" href="{{ item.url }}"><img src="{{ item.cover.path }}" alt="{{ item.cover.alt }}"></a>{% endif %}
              <h3><a href="{{ item.url }}">{{ item.title }}</a></h3>
              <p>{{ item.summary }}</p>
              {% if item.meta %}<div class="card__meta">{{ item.meta }}</div>{% endif %}
              {% if item.hasStack %}<div class="tags">{% for tag in item.stack %}<span>{{ tag }}</span>{% endfor %}</div>{% endif %}
            </article>
            {% endfor %}
          </div>
          {% else %}<div class="empty">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认短文列表模板。</summary>
    private const string NotesTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1>{{ hero.title }}</h1>
          <p class="lead">{{ hero.description }}</p>
        </section>
        <section class="content section">
          {% if hasItems %}
            {% for item in items %}
            <article class="note">
              <bocchi-time datetime="{{ item.isoDate }}" author-time-zone="{{ site.authorTimeZone }}"><time>{{ item.displayDateTime }}</time></bocchi-time>
              <div class="note__body">{{ item.html | html }}</div>
              {% if item.hasMedia %}<div class="media-grid note__media">{% for media in item.media %}<img src="{{ media.path }}" alt="{{ media.alt }}">{% endfor %}</div>{% endif %}
            </article>
            {% endfor %}
          {% else %}<div class="empty">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认友链模板。</summary>
    private const string FriendsTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1>{{ hero.title }}</h1>
          <p class="lead">{{ hero.description }}</p>
        </section>
        <section class="content section">
          {% if hasItems %}
          <div class="list">
            {% for item in items %}
            <a class="list-row friend-row" href="{{ item.url }}">
              <span class="list-row__date">{% if item.hasAvatar %}<img class="friend-avatar" src="{{ item.avatar.path }}" alt="{{ item.avatar.alt }}">{% else %}Link{% endif %}</span>
              <span class="list-row__title">{{ item.title }}</span>
              <span class="list-row__meta">{{ item.summary }}</span>
            </a>
            {% endfor %}
          </div>
          {% else %}<div class="empty">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认文章和作品详情模板。</summary>
    private const string ArticleTemplate = """
        <article class="prose article-header">
          <p><a class="arrow-link" href="{{ section.url }}">Back to {{ section.name }}</a></p>
          <p class="article-meta">{{ section.name }}{% if item.date %} · {{ item.date }}{% endif %}</p>
          <h1>{{ item.title }}</h1>
        </article>
        {% if item.hasCover %}<figure class="prose media-cover"><img src="{{ item.cover.path }}" alt="{{ item.cover.alt }}"></figure>{% endif %}
        <article class="prose prose-body">{{ item.html | html }}</article>
        <nav class="prose section" aria-label="Adjacent content">
          {% if hasPrevious %}<a class="arrow-link" href="{{ previous.url }}">Previous: {{ previous.title }}</a>{% endif %}
          {% if hasNext %}<a class="arrow-link" href="{{ next.url }}">Next: {{ next.title }}</a>{% endif %}
        </nav>
        """;

    /// <summary>默认独立页面模板。</summary>
    private const string StandalonePageTemplate = """
        <article class="prose article-header">
          <p class="eyebrow">Page</p>
          <h1>{{ item.title }}</h1>
        </article>
        <article class="prose prose-body">{{ item.html | html }}</article>
        """;

    /// <summary>默认 404 模板。</summary>
    private const string NotFoundTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1>{{ hero.title }}</h1>
          <p class="lead">{{ hero.description }}</p>
        </section>
        <section class="content section"><a class="arrow-link" href="/">Back to index</a></section>
        """;
}
