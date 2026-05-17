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

        await EnsureFileAsync(Path.Combine(themeRoot, "theme.json"), ThemeJson, cancellationToken, LegacyThemeJson).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "config-schema.json"), ConfigSchemaJson, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "layouts", "base.liquid"), BaseTemplate, cancellationToken, LegacyBaseTemplate).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "index.liquid"), IndexTemplate, cancellationToken, LegacyIndexTemplate).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "posts.liquid"), PostsTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "works.liquid"), WorksTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "notes.liquid"), NotesTemplate, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "friends.liquid"), FriendsTemplate, cancellationToken, LegacyFriendsTemplate).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "article.liquid"), ArticleTemplate, cancellationToken, LegacyArticleTemplate).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "standalone-page.liquid"), StandalonePageTemplate, cancellationToken, LegacyStandalonePageTemplate).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "templates", "pages", "404.liquid"), NotFoundTemplate, cancellationToken, LegacyNotFoundTemplate).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "favicon.svg"), DefaultStaticThemeAssets.FaviconSvg, cancellationToken).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "app.css"), DefaultStaticThemeAssets.Css, cancellationToken, DefaultStaticThemeAssets.LegacyCss).ConfigureAwait(false);
        await EnsureFileAsync(Path.Combine(themeRoot, "assets", "app.js"), DefaultStaticThemeAssets.Js, cancellationToken, DefaultStaticThemeAssets.LegacyJs).ConfigureAwait(false);
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

    /// <summary>写入缺失的默认内容；已存在文件只有在仍等于旧内置默认值时才刷新，避免覆盖用户修改后的 Theme 文件。</summary>
    private static async Task EnsureFileAsync(string path, string content, CancellationToken cancellationToken, params string[] replaceableContents)
    {
        if (File.Exists(path))
        {
            if (replaceableContents.Length > 0)
            {
                var existing = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (replaceableContents.Any(replaceable => string.Equals(existing, replaceable, StringComparison.Ordinal)))
                {
                    await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

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
              },
              {
                "key": "theme.defaultStatic.homeHeroAccent",
                "title": "Home hero accent word",
                "description": "Accent word in the home hero sentence.",
                "defaultValues": {
                  "en-US": "writing",
                  "zh-CN": "写作",
                  "zh-TW": "寫作",
                  "ja-JP": "文章"
                }
              },
              {
                "key": "theme.defaultStatic.homeHeroRest",
                "title": "Home hero rest text",
                "description": "Text after the accent word in the home hero sentence.",
                "defaultValues": {
                  "en-US": ", work, and notes.",
                  "zh-CN": "、作品与札记。",
                  "zh-TW": "、作品與札記。",
                  "ja-JP": "、制作、ノート。"
                }
              },
              {
                "key": "theme.defaultStatic.homeSelectedWriting",
                "title": "Home selected writing heading",
                "description": "Heading for featured posts on the home page.",
                "defaultValues": {
                  "en-US": "Selected Writing",
                  "zh-CN": "精选写作",
                  "zh-TW": "精選寫作",
                  "ja-JP": "選んだ文章"
                }
              },
              {
                "key": "theme.defaultStatic.homeSelectedWork",
                "title": "Home selected work heading",
                "description": "Heading for featured works on the home page.",
                "defaultValues": {
                  "en-US": "Selected Work",
                  "zh-CN": "精选作品",
                  "zh-TW": "精選作品",
                  "ja-JP": "選んだ制作"
                }
              },
              {
                "key": "theme.defaultStatic.homeRecentNotes",
                "title": "Home recent notes heading",
                "description": "Heading for recent notes on the home page.",
                "defaultValues": {
                  "en-US": "Recent Notes",
                  "zh-CN": "最近札记",
                  "zh-TW": "最近札記",
                  "ja-JP": "最近のノート"
                }
              },
              {
                "key": "theme.defaultStatic.all",
                "title": "All link text",
                "description": "Short link text used by home page section links.",
                "defaultValues": {
                  "en-US": "All",
                  "zh-CN": "全部",
                  "zh-TW": "全部",
                  "ja-JP": "すべて"
                }
              },
              {
                "key": "theme.defaultStatic.postsDescription",
                "title": "Posts listing description",
                "description": "Lead text for the posts listing page.",
                "defaultValues": {
                  "en-US": "Long-form notes and essays.",
                  "zh-CN": "长文章、随笔与记录。",
                  "zh-TW": "長文章、隨筆與記錄。",
                  "ja-JP": "長めのノートとエッセイ。"
                }
              },
              {
                "key": "theme.defaultStatic.worksDescription",
                "title": "Works listing description",
                "description": "Lead text for the works listing page.",
                "defaultValues": {
                  "en-US": "Selected projects and experiments.",
                  "zh-CN": "选中的项目与实验。",
                  "zh-TW": "選中的專案與實驗。",
                  "ja-JP": "選んだプロジェクトと実験。"
                }
              },
              {
                "key": "theme.defaultStatic.notesDescription",
                "title": "Notes listing description",
                "description": "Lead text for the notes listing page.",
                "defaultValues": {
                  "en-US": "Short updates in plain text.",
                  "zh-CN": "用纯文本记录的短更新。",
                  "zh-TW": "用純文字記錄的短更新。",
                  "ja-JP": "プレーンテキストの短い更新。"
                }
              },
              {
                "key": "theme.defaultStatic.friendsDescription",
                "title": "Friends listing description",
                "description": "Lead text for the friends listing page.",
                "defaultValues": {
                  "en-US": "People and sites worth visiting.",
                  "zh-CN": "值得拜访的人与站点。",
                  "zh-TW": "值得拜訪的人與站點。",
                  "ja-JP": "訪ねたい人とサイト。"
                }
              },
              {
                "key": "theme.defaultStatic.linkLabel",
                "title": "Link label",
                "description": "Compact label used when a friend link has no avatar.",
                "defaultValues": {
                  "en-US": "Link",
                  "zh-CN": "链接",
                  "zh-TW": "連結",
                  "ja-JP": "リンク"
                }
              },
              {
                "key": "theme.defaultStatic.pageLabel",
                "title": "Page label",
                "description": "Eyebrow label for standalone pages.",
                "defaultValues": {
                  "en-US": "Page",
                  "zh-CN": "页面",
                  "zh-TW": "頁面",
                  "ja-JP": "ページ"
                }
              },
              {
                "key": "theme.defaultStatic.articleBackPrefix",
                "title": "Article back prefix",
                "description": "Text before the section name in article back links.",
                "defaultValues": {
                  "en-US": "Back to",
                  "zh-CN": "返回",
                  "zh-TW": "返回",
                  "ja-JP": "戻る"
                }
              },
              {
                "key": "theme.defaultStatic.notFoundDescription",
                "title": "404 description",
                "description": "Lead text for the 404 page.",
                "defaultValues": {
                  "en-US": "This page is not in the static output.",
                  "zh-CN": "这个页面不在静态输出中。",
                  "zh-TW": "這個頁面不在靜態輸出中。",
                  "ja-JP": "このページは静的出力にありません。"
                }
              },
              {
                "key": "theme.defaultStatic.toggleAppearance",
                "title": "Appearance toggle aria label",
                "description": "Accessible label for the appearance toggle button.",
                "defaultValues": {
                  "en-US": "Toggle appearance",
                  "zh-CN": "切换外观",
                  "zh-TW": "切換外觀",
                  "ja-JP": "外観を切り替える"
                }
              },
              {
                "key": "theme.defaultStatic.openMenu",
                "title": "Mobile menu aria label",
                "description": "Accessible label for the mobile menu button.",
                "defaultValues": {
                  "en-US": "Open menu",
                  "zh-CN": "打开菜单",
                  "zh-TW": "開啟選單",
                  "ja-JP": "メニューを開く"
                }
              },
              {
                "key": "theme.defaultStatic.languageLabel",
                "title": "Language menu label",
                "description": "Accessible label for the frontend language menu.",
                "defaultValues": {
                  "en-US": "Language",
                  "zh-CN": "语言",
                  "zh-TW": "語言",
                  "ja-JP": "言語"
                }
              },
              {
                "key": "theme.defaultStatic.appearanceLabel",
                "title": "Appearance menu label",
                "description": "Accessible label for the frontend appearance menu.",
                "defaultValues": {
                  "en-US": "Appearance",
                  "zh-CN": "外观",
                  "zh-TW": "外觀",
                  "ja-JP": "外観"
                }
              },
              {
                "key": "theme.defaultStatic.appearanceAuto",
                "title": "Auto appearance option",
                "description": "Label for the automatic appearance option.",
                "defaultValues": {
                  "en-US": "Auto",
                  "zh-CN": "自动",
                  "zh-TW": "自動",
                  "ja-JP": "自動"
                }
              },
              {
                "key": "theme.defaultStatic.appearanceLight",
                "title": "Light appearance option",
                "description": "Label for the light appearance option.",
                "defaultValues": {
                  "en-US": "Light",
                  "zh-CN": "浅色",
                  "zh-TW": "淺色",
                  "ja-JP": "ライト"
                }
              },
              {
                "key": "theme.defaultStatic.appearanceDark",
                "title": "Dark appearance option",
                "description": "Label for the dark appearance option.",
                "defaultValues": {
                  "en-US": "Dark",
                  "zh-CN": "深色",
                  "zh-TW": "深色",
                  "ja-JP": "ダーク"
                }
              }
            ]
          }
        }
        """;

    /// <summary>M6 前的默认 Theme manifest；仅用于刷新未被用户修改过的旧物化文件。</summary>
    private const string LegacyThemeJson = """
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
        <html lang="{{ localization.currentLanguage }}">
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
                {% for item in navigation %}<a href="{{ item.href }}"{% if item.current %} aria-current="page"{% endif %} data-bocchi-i18n="{{ item.i18nKey }}">{{ item.label }}</a>{% endfor %}
              </nav>
              <div class="toolbar">
                <details class="theme-menu" data-bocchi-language-control>
                  <summary aria-label="{{ text.languageLabel }}" title="{{ text.languageLabel }}">
                    <span class="theme-menu__icon" aria-hidden="true">文</span>
                    <span class="theme-menu__current" data-bocchi-language-summary>{{ localization.currentLanguage }}</span>
                    <span class="theme-menu__chevron" aria-hidden="true">⌄</span>
                  </summary>
                  <div class="theme-menu__menu" role="menu" aria-label="{{ text.languageLabel }}">
                    {% for language in localization.languages %}
                    <button class="theme-menu__option" type="button" role="menuitemradio" data-bocchi-language-option="{{ language.code }}" aria-current="{% if language.code == localization.currentLanguage %}true{% else %}false{% endif %}">
                      <span>{{ language.nativeName }}</span>
                      <small>{{ language.code }}</small>
                    </button>
                    {% endfor %}
                  </div>
                </details>
                <details class="theme-menu" data-bocchi-appearance-control>
                  <summary aria-label="{{ text.appearanceLabel }}" title="{{ text.appearanceLabel }}">
                    <span class="theme-menu__appearance-icon theme-menu__appearance-icon--auto" aria-hidden="true">◐</span>
                    <span class="theme-menu__appearance-icon theme-menu__appearance-icon--light" aria-hidden="true">☀</span>
                    <span class="theme-menu__appearance-icon theme-menu__appearance-icon--dark" aria-hidden="true">☾</span>
                    <span class="theme-menu__chevron" aria-hidden="true">⌄</span>
                  </summary>
                  <div class="theme-menu__menu" role="menu" aria-label="{{ text.appearanceLabel }}">
                    <button class="theme-menu__option" type="button" role="menuitemradio" data-bocchi-appearance-option="auto" aria-current="true"><span data-bocchi-i18n="theme.defaultStatic.appearanceAuto">{{ text.appearanceAuto }}</span></button>
                    <button class="theme-menu__option" type="button" role="menuitemradio" data-bocchi-appearance-option="light" aria-current="false"><span data-bocchi-i18n="theme.defaultStatic.appearanceLight">{{ text.appearanceLight }}</span></button>
                    <button class="theme-menu__option" type="button" role="menuitemradio" data-bocchi-appearance-option="dark" aria-current="false"><span data-bocchi-i18n="theme.defaultStatic.appearanceDark">{{ text.appearanceDark }}</span></button>
                  </div>
                </details>
                <button class="icon-button mobile-toggle" type="button" data-mobile-toggle aria-expanded="false" aria-label="{{ text.openMenu }}">☰</button>
              </div>
            </div>
            <nav class="mobile-nav" data-mobile-nav aria-label="Mobile primary">
              {% for item in navigation %}<a href="{{ item.href }}"{% if item.current %} aria-current="page"{% endif %} data-bocchi-i18n="{{ item.i18nKey }}">{{ item.label }}</a>{% endfor %}
            </nav>
          </header>
          <main>{{ content | html }}</main>
          <footer class="footer">
            <div class="footer__inner">
              <span>{{ site.copyrightNotice }}</span>
              <span data-bocchi-i18n="theme.defaultStatic.colophonBuiltWith">{{ text.colophonBuiltWith }}</span>
              <span><a href="/feed.xml">RSS</a> · <a href="/sitemap.xml">Sitemap</a></span>
            </div>
          </footer>
          <script type="application/json" id="bocchi-i18n-data">{{ localization.textJson | html }}</script>
          <script type="module" src="/assets/app.js"></script>
        </body>
        </html>
        """;

    /// <summary>默认首页模板。</summary>
    private const string IndexTemplate = """
        <section class="hero container">
          <p class="eyebrow"><span data-bocchi-i18n="menu.home">{{ page.title }}</span> · {{ site.authorTimeZone }}</p>
          <h1>{{ site.title }} <em data-bocchi-i18n="theme.defaultStatic.homeHeroAccent">{{ text.homeHeroAccent }}</em><span data-bocchi-i18n="theme.defaultStatic.homeHeroRest">{{ text.homeHeroRest }}</span></h1>
          <p class="lead">{{ site.description }}</p>
          <div class="meta-row"><span>{{ site.authorName }}</span><span data-bocchi-current-language>{{ localization.currentLanguage }}</span><span>{{ site.baseUrl }}</span></div>
        </section>
        <section class="content section">
          <div class="section-head"><h2 data-bocchi-i18n="theme.defaultStatic.homeSelectedWriting">{{ text.homeSelectedWriting }}</h2><a class="arrow-link" href="/posts/" data-bocchi-i18n="theme.defaultStatic.all">{{ text.all }}</a></div>
          {% if hasFeaturedPosts %}
          <div class="list">
            {% for item in featuredPosts %}<a class="list-row" href="{{ item.url }}"><span class="list-row__date">{{ item.yearMonth }}</span><span class="list-row__title">{{ item.title }}</span><span class="list-row__meta">{{ item.meta }}</span></a>{% endfor %}
          </div>
          {% else %}<div class="empty" data-bocchi-i18n="theme.defaultStatic.emptyList">{{ text.emptyList }}</div>{% endif %}
        </section>
        <section class="content section">
          <div class="section-head"><h2 data-bocchi-i18n="theme.defaultStatic.homeSelectedWork">{{ text.homeSelectedWork }}</h2><a class="arrow-link" href="/works/" data-bocchi-i18n="theme.defaultStatic.all">{{ text.all }}</a></div>
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
          {% else %}<div class="empty" data-bocchi-i18n="theme.defaultStatic.emptyList">{{ text.emptyList }}</div>{% endif %}
        </section>
        <section class="content section">
          <div class="section-head"><h2 data-bocchi-i18n="theme.defaultStatic.homeRecentNotes">{{ text.homeRecentNotes }}</h2><a class="arrow-link" href="/notes/" data-bocchi-i18n="theme.defaultStatic.all">{{ text.all }}</a></div>
          {% if hasRecentNotes %}
            {% for item in recentNotes %}<article class="note"><bocchi-time datetime="{{ item.isoDate }}" author-time-zone="{{ site.authorTimeZone }}"><time>{{ item.displayDateTime }}</time></bocchi-time><div class="note__body">{{ item.html | html }}</div></article>{% endfor %}
          {% else %}<div class="empty" data-bocchi-i18n="theme.defaultStatic.emptyList">{{ text.emptyList }}</div>{% endif %}
        </section>
        {% if showFriends %}
        <section class="content section">
          <div class="section-head"><h2 data-bocchi-i18n="menu.friends">{{ text.homeFriends }}</h2><a class="arrow-link" href="/friends/" data-bocchi-i18n="theme.defaultStatic.all">{{ text.all }}</a></div>
          {% if hasFriends %}
          <div class="list">
            {% for item in friends %}<a class="list-row" href="{{ item.url }}"><span class="list-row__date" data-bocchi-i18n="theme.defaultStatic.linkLabel">{{ text.linkLabel }}</span><span class="list-row__title">{{ item.title }}</span><span class="list-row__meta">{{ item.summary }}</span></a>{% endfor %}
          </div>
          {% else %}<div class="empty" data-bocchi-i18n="theme.defaultStatic.emptyList">{{ text.emptyList }}</div>{% endif %}
        </section>
        {% endif %}
        """;

    /// <summary>默认文章列表模板。</summary>
    private const string PostsTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1{% if hero.titleKey %} data-bocchi-i18n="{{ hero.titleKey }}"{% endif %}>{{ hero.title }}</h1>
          <p class="lead"{% if hero.descriptionKey %} data-bocchi-i18n="{{ hero.descriptionKey }}"{% endif %}>{{ hero.description }}</p>
        </section>
        <section class="content section">
          {% if hasItems %}
          <div class="list">
            {% for item in items %}<a class="list-row" href="{{ item.url }}"><span class="list-row__date">{{ item.yearMonth }}</span><span class="list-row__title">{{ item.title }}</span><span class="list-row__meta">{{ item.meta }}</span></a>{% endfor %}
          </div>
          {% else %}<div class="empty" data-bocchi-i18n="{{ emptyTextKey }}">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认作品列表模板。</summary>
    private const string WorksTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1{% if hero.titleKey %} data-bocchi-i18n="{{ hero.titleKey }}"{% endif %}>{{ hero.title }}</h1>
          <p class="lead"{% if hero.descriptionKey %} data-bocchi-i18n="{{ hero.descriptionKey }}"{% endif %}>{{ hero.description }}</p>
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
          {% else %}<div class="empty" data-bocchi-i18n="{{ emptyTextKey }}">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认短文列表模板。</summary>
    private const string NotesTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1{% if hero.titleKey %} data-bocchi-i18n="{{ hero.titleKey }}"{% endif %}>{{ hero.title }}</h1>
          <p class="lead"{% if hero.descriptionKey %} data-bocchi-i18n="{{ hero.descriptionKey }}"{% endif %}>{{ hero.description }}</p>
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
          {% else %}<div class="empty" data-bocchi-i18n="{{ emptyTextKey }}">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认友链模板。</summary>
    private const string FriendsTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1{% if hero.titleKey %} data-bocchi-i18n="{{ hero.titleKey }}"{% endif %}>{{ hero.title }}</h1>
          <p class="lead"{% if hero.descriptionKey %} data-bocchi-i18n="{{ hero.descriptionKey }}"{% endif %}>{{ hero.description }}</p>
        </section>
        <section class="content section">
          {% if hasItems %}
          <div class="list">
            {% for item in items %}
            <a class="list-row friend-row" href="{{ item.url }}">
              <span class="list-row__date">{% if item.hasAvatar %}<img class="friend-avatar" src="{{ item.avatar.path }}" alt="{{ item.avatar.alt }}">{% else %}<span data-bocchi-i18n="theme.defaultStatic.linkLabel">{{ text.linkLabel }}</span>{% endif %}</span>
              <span class="list-row__title">{{ item.title }}</span>
              <span class="list-row__meta">{{ item.summary }}</span>
            </a>
            {% endfor %}
          </div>
          {% else %}<div class="empty" data-bocchi-i18n="{{ emptyTextKey }}">{{ emptyText }}</div>{% endif %}
        </section>
        """;

    /// <summary>默认文章和作品详情模板。</summary>
    private const string ArticleTemplate = """
        <article class="prose article-header">
          <p><a class="arrow-link" href="{{ section.url }}"><span data-bocchi-i18n="theme.defaultStatic.articleBackPrefix">{{ text.articleBackPrefix }}</span> {{ section.name }}</a></p>
          <p class="article-meta">{{ section.name }}{% if item.date %} · {{ item.date }}{% endif %}</p>
          <h1>{{ item.title }}</h1>
        </article>
        {% if item.hasCover %}<figure class="prose media-cover"><img src="{{ item.cover.path }}" alt="{{ item.cover.alt }}"></figure>{% endif %}
        <article class="prose prose-body">{{ item.html | html }}</article>
        <nav class="prose section" aria-label="Adjacent content">
          {% if hasPrevious %}<a class="arrow-link" href="{{ previous.url }}"><span data-bocchi-i18n="common.previous">{{ text.previous }}</span>: {{ previous.title }}</a>{% endif %}
          {% if hasNext %}<a class="arrow-link" href="{{ next.url }}"><span data-bocchi-i18n="common.next">{{ text.next }}</span>: {{ next.title }}</a>{% endif %}
        </nav>
        """;

    /// <summary>默认独立页面模板。</summary>
    private const string StandalonePageTemplate = """
        <article class="prose article-header">
          <p class="eyebrow" data-bocchi-i18n="theme.defaultStatic.pageLabel">{{ text.pageLabel }}</p>
          <h1>{{ item.title }}</h1>
        </article>
        <article class="prose prose-body">{{ item.html | html }}</article>
        """;

    /// <summary>默认 404 模板。</summary>
    private const string NotFoundTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1>{{ hero.title }}</h1>
          <p class="lead" data-bocchi-i18n="{{ hero.descriptionKey }}">{{ hero.description }}</p>
        </section>
        <section class="content section"><a class="arrow-link" href="/" data-bocchi-i18n="common.backHome">{{ text.backHome }}</a></section>
        """;

    /// <summary>M6 前的全局布局模板；仅用于刷新未被用户修改过的旧物化文件。</summary>
    private const string LegacyBaseTemplate = """
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
              <span>{{ site.copyrightNotice }}</span>
              <span><a href="/feed.xml">RSS</a> · <a href="/sitemap.xml">Sitemap</a></span>
            </div>
          </footer>
          <script type="module" src="/assets/app.js"></script>
        </body>
        </html>
        """;

    /// <summary>M6 前的首页模板；仅用于刷新未被用户修改过的旧物化文件。</summary>
    private const string LegacyIndexTemplate = """
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

    /// <summary>M6 前的友链模板；仅用于刷新未被用户修改过的旧物化文件。</summary>
    private const string LegacyFriendsTemplate = """
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

    /// <summary>M6 前的详情模板；仅用于刷新未被用户修改过的旧物化文件。</summary>
    private const string LegacyArticleTemplate = """
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

    /// <summary>M6 前的独立页面模板；仅用于刷新未被用户修改过的旧物化文件。</summary>
    private const string LegacyStandalonePageTemplate = """
        <article class="prose article-header">
          <p class="eyebrow">Page</p>
          <h1>{{ item.title }}</h1>
        </article>
        <article class="prose prose-body">{{ item.html | html }}</article>
        """;

    /// <summary>M6 前的 404 模板；仅用于刷新未被用户修改过的旧物化文件。</summary>
    private const string LegacyNotFoundTemplate = """
        <section class="content section">
          <p class="eyebrow">{{ hero.number }}</p>
          <h1>{{ hero.title }}</h1>
          <p class="lead">{{ hero.description }}</p>
        </section>
        <section class="content section"><a class="arrow-link" href="/">Back to index</a></section>
        """;
}
