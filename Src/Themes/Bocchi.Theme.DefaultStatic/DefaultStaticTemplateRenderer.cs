using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Bocchi.GeneratorContract;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>内置默认静态 Theme renderer。当前实现先用 typed helper 输出完整静态页面，后续再把模板主体迁入 Fluid。</summary>
public sealed class DefaultStaticTemplateRenderer
{
    /// <summary>执行一次默认 Theme 渲染。</summary>
    public static async Task RenderAsync(DefaultStaticRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Manifest.Id, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal))
        {
            throw new DefaultStaticThemeException($"默认 renderer 只能处理 '{DefaultStaticThemeDefinition.ThemeId}'，实际为 '{request.Manifest.Id}'。");
        }

        var input = await ReadInputAsync(request.InputDirectory, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(request.OutputDirectory);

        var site = SiteInfo.From(input.ThemeContext);
        var visiblePosts = FilterVisible(input.Posts, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visiblePages = FilterVisible(input.Pages, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();
        var visibleWorks = FilterVisible(input.Works, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleNotes = FilterVisible(input.Notes, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleFriends = FilterVisible(input.Friends, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();

        await WriteAssetsAsync(request, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "index.html", RenderHome(site, input, visiblePosts, visibleWorks, visibleNotes, visibleFriends), cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "posts/index.html", RenderPostList(site, visiblePosts), cancellationToken).ConfigureAwait(false);
        await WritePostDetailsAsync(request.OutputDirectory, site, visiblePosts, cancellationToken).ConfigureAwait(false);
        await WriteStandalonePagesAsync(request.OutputDirectory, site, visiblePages, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "works/index.html", RenderWorkList(site, visibleWorks), cancellationToken).ConfigureAwait(false);
        await WriteWorkDetailsAsync(request.OutputDirectory, site, visibleWorks, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "notes/index.html", RenderNotes(site, visibleNotes, null), cancellationToken).ConfigureAwait(false);
        await WriteNoteYearPagesAsync(request.OutputDirectory, site, visibleNotes, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "friends/index.html", RenderFriends(site, visibleFriends), cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "404.html", RenderNotFound(site), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取所有 M5 默认 Theme 需要的 Theme Contract 输入。</summary>
    private static async Task<ThemeInputSet> ReadInputAsync(string inputDirectory, CancellationToken cancellationToken)
    {
        return new ThemeInputSet(
            await ReadDataAsync(inputDirectory, "theme-context.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "posts.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "pages.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "works.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "notes.json", cancellationToken).ConfigureAwait(false),
            await ReadArrayAsync(inputDirectory, "friends.json", cancellationToken).ConfigureAwait(false));
    }

    /// <summary>读取单个 envelope 的 <c>data</c> 对象，并验证 Theme Contract 版本。</summary>
    private static async Task<JsonElement> ReadDataAsync(string inputDirectory, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(inputDirectory, fileName);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
        try
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var version = root.GetProperty("contractVersion").GetString();
            if (!string.Equals(version, ThemeContractVersion.Current, StringComparison.Ordinal))
            {
                throw new DefaultStaticThemeException($"Theme 输入 '{fileName}' contractVersion='{version}'，当前只支持 '{ThemeContractVersion.Current}'。");
            }

            return root.GetProperty("data").Clone();
        }
        catch (JsonException ex)
        {
            throw new DefaultStaticThemeException($"Theme 输入 '{fileName}' 不是合法 JSON。", ex);
        }
    }

    /// <summary>读取 envelope 的 <c>data</c> 数组。</summary>
    private static async Task<JsonElement[]> ReadArrayAsync(string inputDirectory, string fileName, CancellationToken cancellationToken)
    {
        var data = await ReadDataAsync(inputDirectory, fileName, cancellationToken).ConfigureAwait(false);
        if (data.ValueKind != JsonValueKind.Array)
        {
            throw new DefaultStaticThemeException($"Theme 输入 '{fileName}' 的 data 必须是 array。");
        }

        return data.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    /// <summary>复制用户可覆盖资产；缺失时回退到内置资产。</summary>
    private static async Task WriteAssetsAsync(DefaultStaticRenderRequest request, CancellationToken cancellationToken)
    {
        var assetOutput = Path.Combine(request.OutputDirectory, "assets");
        Directory.CreateDirectory(assetOutput);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "favicon.svg", DefaultStaticThemeAssets.FaviconSvg, cancellationToken).ConfigureAwait(false);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "app.css", DefaultStaticThemeAssets.Css, cancellationToken).ConfigureAwait(false);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "app.js", DefaultStaticThemeAssets.Js, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>优先复制工作区 Theme asset，保证用户修改 CSS/JS 后构建能生效。</summary>
    private static async Task CopyOrWriteAssetAsync(
        string themeRoot,
        string outputDirectory,
        string fileName,
        string fallbackContent,
        CancellationToken cancellationToken)
    {
        var source = Path.Combine(themeRoot, "assets", fileName);
        var destination = Path.Combine(outputDirectory, fileName);
        if (File.Exists(source))
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return;
        }

        await File.WriteAllTextAsync(destination, fallbackContent, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>写入一个相对 Theme 输出目录的 HTML 文件。</summary>
    private static async Task WritePageAsync(string outputDirectory, string relativePath, string html, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>渲染首页。</summary>
    private static string RenderHome(
        SiteInfo site,
        ThemeInputSet input,
        JsonElement[] posts,
        JsonElement[] works,
        JsonElement[] notes,
        JsonElement[] friends)
    {
        var featuredPosts = Limit(posts, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredPosts"], 5));
        var featuredWorks = Limit(works, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredWorks"], 4));
        var recentNotes = Limit(notes, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "recentNotes"], 3));
        var showFriends = GetConfigBool(input.ThemeContext, ["theme", "config", "home", "showFriends"], true);

        var body = $"""
            <section class="hero container">
              <p class="eyebrow">Index · {H(site.AuthorTimeZone)}</p>
              <h1>{H(site.Title)} <em>writing</em>, work, and notes.</h1>
              <p class="lead">{H(site.DescriptionOrFallback)}</p>
              <div class="meta-row"><span>{H(site.AuthorName)}</span><span>{H(site.Language)}</span><span>{H(site.BaseUrl)}</span></div>
            </section>
            {RenderSection("Selected Writing", "/posts/", RenderRows(featuredPosts, "No writing yet."))}
            {RenderSection("Selected Work", "/works/", RenderCards(featuredWorks, "No work entries yet."))}
            {RenderSection("Recent Notes", "/notes/", RenderNoteItems(recentNotes, "No notes yet."))}
            {(showFriends ? RenderSection("Friends", "/friends/", RenderFriendLinks(Limit(friends, 6), "No friend links yet.")) : string.Empty)}
            """;
        return Layout(site, "Index", "/", body);
    }

    /// <summary>渲染文章列表页。</summary>
    private static string RenderPostList(SiteInfo site, JsonElement[] posts)
        => Layout(site, "Writing", "/posts/", PageHero("Writing", "Long-form notes and essays.", "02") + RenderRows(posts, "No writing yet."));

    /// <summary>渲染作品列表页。</summary>
    private static string RenderWorkList(SiteInfo site, JsonElement[] works)
        => Layout(site, "Work", "/works/", PageHero("Work", "Selected projects and experiments.", "03") + RenderCards(works, "No work entries yet."));

    /// <summary>渲染短文总览或年份页。</summary>
    private static string RenderNotes(SiteInfo site, JsonElement[] notes, string? year)
    {
        var title = year is null ? "Notes" : $"Notes {year}";
        return Layout(site, title, year is null ? "/notes/" : $"/notes/{year}/", PageHero(title, "Short updates in plain text.", "04") + RenderNoteItems(notes, "No notes yet."));
    }

    /// <summary>渲染友链页。</summary>
    private static string RenderFriends(SiteInfo site, JsonElement[] friends)
        => Layout(site, "Friends", "/friends/", PageHero("Friends", "People and sites worth visiting.", "05") + RenderFriendLinks(friends, "No friend links yet."));

    /// <summary>渲染 404 页面。</summary>
    private static string RenderNotFound(SiteInfo site)
        => Layout(site, "Not Found", string.Empty, PageHero("404", "This page is not in the static output.", "404") + """<section class="content section"><a class="arrow-link" href="/">Back to index</a></section>""");

    /// <summary>渲染文章详情页。</summary>
    private static async Task WritePostDetailsAsync(string outputDirectory, SiteInfo site, JsonElement[] posts, CancellationToken cancellationToken)
    {
        for (var i = 0; i < posts.Length; i++)
        {
            var post = posts[i];
            var body = RenderArticle(site, "Writing", "/posts/", post, Previous(posts, i), Next(posts, i));
            await WritePageAsync(outputDirectory, ToOutputPath(GetString(post, "url")), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染独立页面。</summary>
    private static async Task WriteStandalonePagesAsync(string outputDirectory, SiteInfo site, JsonElement[] pages, CancellationToken cancellationToken)
    {
        foreach (var page in pages)
        {
            var title = GetTitle(page);
            var body = $"""
                <article class="prose article-header">
                  <p class="eyebrow">Page</p>
                  <h1>{H(title)}</h1>
                </article>
                <article class="prose prose-body">{RawHtml(page)}</article>
                """;
            await WritePageAsync(outputDirectory, ToOutputPath(GetString(page, "url")), Layout(site, title, GetString(page, "url"), body), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染作品详情页。</summary>
    private static async Task WriteWorkDetailsAsync(string outputDirectory, SiteInfo site, JsonElement[] works, CancellationToken cancellationToken)
    {
        for (var i = 0; i < works.Length; i++)
        {
            var work = works[i];
            var body = RenderArticle(site, "Work", "/works/", work, Previous(works, i), Next(works, i));
            await WritePageAsync(outputDirectory, ToOutputPath(GetString(work, "url")), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>按年份渲染短文页。</summary>
    private static async Task WriteNoteYearPagesAsync(string outputDirectory, SiteInfo site, JsonElement[] notes, CancellationToken cancellationToken)
    {
        foreach (var group in notes.GroupBy(note => GetString(note, "year")).Where(group => !string.IsNullOrWhiteSpace(group.Key)))
        {
            await WritePageAsync(outputDirectory, $"notes/{group.Key}/index.html", RenderNotes(site, group.ToArray(), group.Key), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>渲染文章或作品详情。</summary>
    private static string RenderArticle(SiteInfo site, string sectionName, string sectionUrl, JsonElement item, JsonElement? previous, JsonElement? next)
    {
        var title = GetTitle(item);
        var date = FormatDate(GetContentDate(item), site.AuthorTimeZone);
        var meta = string.IsNullOrWhiteSpace(date) ? sectionName : $"{sectionName} · {date}";
        var body = $"""
            <article class="prose article-header">
              <p><a class="arrow-link" href="{H(sectionUrl)}">Back to {H(sectionName)}</a></p>
              <p class="article-meta">{H(meta)}</p>
              <h1>{H(title)}</h1>
            </article>
            <article class="prose prose-body">{RawHtml(item)}</article>
            <nav class="prose section" aria-label="Adjacent content">
              {(previous is null ? string.Empty : $"""<a class="arrow-link" href="{H(GetString(previous.Value, "url"))}">Previous: {H(GetTitle(previous.Value))}</a>""")}
              {(next is null ? string.Empty : $"""<a class="arrow-link" href="{H(GetString(next.Value, "url"))}">Next: {H(GetTitle(next.Value))}</a>""")}
            </nav>
            """;
        return Layout(site, title, GetString(item, "url"), body);
    }

    /// <summary>生成页面通用 Hero。</summary>
    private static string PageHero(string title, string description, string number)
        => $"""
            <section class="content section">
              <p class="eyebrow">{H(number)}</p>
              <h1>{H(title)}</h1>
              <p class="lead">{H(description)}</p>
            </section>
            """;

    /// <summary>生成首页分区。</summary>
    private static string RenderSection(string title, string href, string content)
        => $"""
            <section class="content section">
              <div class="section-head"><h2>{H(title)}</h2><a class="arrow-link" href="{H(href)}">All</a></div>
              {content}
            </section>
            """;

    /// <summary>渲染行式内容列表。</summary>
    private static string RenderRows(IEnumerable<JsonElement> items, string emptyText)
    {
        var rows = items.Select(item =>
        {
            var date = FormatYearMonth(GetContentDate(item));
            var meta = GetString(item, "category");
            if (string.IsNullOrWhiteSpace(meta))
            {
                meta = string.Join(" · ", GetStringArray(item, "tags").Take(2));
            }

            return $"""<a class="list-row" href="{H(GetString(item, "url"))}"><span class="list-row__date">{H(date)}</span><span class="list-row__title">{H(GetTitle(item))}</span><span class="list-row__meta">{H(meta)}</span></a>""";
        }).ToArray();
        return rows.Length == 0 ? $"""<div class="empty">{H(emptyText)}</div>""" : $"""<div class="list">{string.Concat(rows)}</div>""";
    }

    /// <summary>渲染作品卡片。</summary>
    private static string RenderCards(IEnumerable<JsonElement> items, string emptyText)
    {
        var cards = items.Select(item =>
        {
            var tags = string.Concat(GetStringArray(item, "stack").Take(4).Select(tag => $"""<span>{H(tag)}</span>"""));
            return $"""<article class="card"><h3><a href="{H(GetString(item, "url"))}">{H(GetTitle(item))}</a></h3><p>{H(GetSummary(item))}</p><div class="tags">{tags}</div></article>""";
        }).ToArray();
        return cards.Length == 0 ? $"""<div class="empty">{H(emptyText)}</div>""" : $"""<div class="grid">{string.Concat(cards)}</div>""";
    }

    /// <summary>渲染短文条目。</summary>
    private static string RenderNoteItems(IEnumerable<JsonElement> notes, string emptyText)
    {
        var items = notes.Select(note =>
        {
            var publishedAt = GetContentDate(note);
            var iso = publishedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
            var display = publishedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "undated";
            return $"""<article class="note"><bocchi-time datetime="{H(iso)}" author-time-zone="UTC"><time>{H(display)}</time></bocchi-time><div class="note__body">{RawHtml(note)}</div></article>""";
        }).ToArray();
        return items.Length == 0 ? $"""<div class="empty">{H(emptyText)}</div>""" : string.Concat(items);
    }

    /// <summary>渲染友链列表。</summary>
    private static string RenderFriendLinks(IEnumerable<JsonElement> friends, string emptyText)
    {
        var rows = friends.Select(friend =>
        {
            var url = GetString(friend, "url");
            return $"""<a class="list-row" href="{H(url)}"><span class="list-row__date">Link</span><span class="list-row__title">{H(GetTitle(friend))}</span><span class="list-row__meta">{H(GetSummary(friend))}</span></a>""";
        }).ToArray();
        return rows.Length == 0 ? $"""<div class="empty">{H(emptyText)}</div>""" : $"""<div class="list">{string.Concat(rows)}</div>""";
    }

    /// <summary>生成完整 HTML 文档。</summary>
    private static string Layout(SiteInfo site, string title, string currentPath, string body)
    {
        var fullTitle = string.Equals(title, "Index", StringComparison.Ordinal) ? site.Title : $"{title} · {site.Title}";
        return $$"""
            <!doctype html>
            <html lang="{{H(site.Language)}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="description" content="{{H(site.DescriptionOrFallback)}}">
              <title>{{H(fullTitle)}}</title>
              <link rel="icon" type="image/svg+xml" href="/assets/favicon.svg">
              <link rel="stylesheet" href="/assets/app.css">
              <style>:root{--accent: {{H(site.AccentColor)}};}</style>
            </head>
            <body>
              <header class="topbar">
                <div class="topbar__inner">
                  <a class="wordmark" href="/">{{H(site.Title)}}</a>
                  <nav class="nav" aria-label="Primary">
                    {{NavLink("/", "Index", currentPath)}}
                    {{NavLink("/posts/", "Writing", currentPath)}}
                    {{NavLink("/works/", "Work", currentPath)}}
                    {{NavLink("/notes/", "Notes", currentPath)}}
                    {{NavLink("/friends/", "Friends", currentPath)}}
                  </nav>
                  <div class="toolbar">
                    <button class="icon-button" type="button" data-theme-toggle aria-label="Toggle appearance">◐</button>
                    <button class="icon-button mobile-toggle" type="button" data-mobile-toggle aria-expanded="false" aria-label="Open menu">☰</button>
                  </div>
                </div>
                <nav class="mobile-nav" data-mobile-nav aria-label="Mobile primary">
                  <a href="/">Index</a><a href="/posts/">Writing</a><a href="/works/">Work</a><a href="/notes/">Notes</a><a href="/friends/">Friends</a>
                </nav>
              </header>
              <main>{{body}}</main>
              <footer class="footer"><div class="footer__inner"><span>{{H(site.Title)}} · {{DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture)}}</span><span><a href="/feed.xml">RSS</a> · <a href="/sitemap.xml">Sitemap</a></span></div></footer>
              <script type="module" src="/assets/app.js"></script>
            </body>
            </html>
            """;
    }

    /// <summary>生成导航链接并标记当前页。</summary>
    private static string NavLink(string href, string label, string currentPath)
    {
        var current = string.Equals(href, currentPath, StringComparison.Ordinal) ||
            href != "/" && currentPath.StartsWith(href, StringComparison.Ordinal);
        var currentAttribute = current ? " aria-current=\"page\"" : string.Empty;
        return $"""<a href="{H(href)}"{currentAttribute}>{H(label)}</a>""";
    }

    /// <summary>过滤发布内容；预览构建可以通过 includeDrafts 纳入草稿。</summary>
    private static IEnumerable<JsonElement> FilterVisible(IEnumerable<JsonElement> items, bool includeDrafts)
        => items.Where(item => includeDrafts || string.Equals(GetString(item, "status"), "published", StringComparison.OrdinalIgnoreCase));

    /// <summary>限制首页展示数量。</summary>
    private static JsonElement[] Limit(JsonElement[] items, int count)
        => items.Take(Math.Max(0, count)).ToArray();

    /// <summary>获取前一条内容。</summary>
    private static JsonElement? Previous(JsonElement[] items, int index)
        => index > 0 ? items[index - 1] : null;

    /// <summary>获取后一条内容。</summary>
    private static JsonElement? Next(JsonElement[] items, int index)
        => index + 1 < items.Length ? items[index + 1] : null;

    /// <summary>把 Theme URL 转为输出目录内的 index.html 路径。</summary>
    private static string ToOutputPath(string url)
        => string.IsNullOrWhiteSpace(url) || url == "/" ? "index.html" : url.Trim('/').Trim() + "/index.html";

    /// <summary>读取内容标题。</summary>
    private static string GetTitle(JsonElement item)
        => GetString(item, "title", GetString(item, "name", "Untitled"));

    /// <summary>读取内容摘要。</summary>
    private static string GetSummary(JsonElement item)
        => GetString(item, "summary", GetString(item, "description", GetString(item, "excerpt")));

    /// <summary>读取排序字段。</summary>
    private static int GetOrder(JsonElement item)
        => item.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number ? order.GetInt32() : 0;

    /// <summary>读取内容发布时间或更新时间。</summary>
    private static DateTimeOffset? GetContentDate(JsonElement item)
    {
        foreach (var key in new[] { "publishedAt", "updatedAt" })
        {
            var raw = GetString(item, key);
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>读取字符串属性。</summary>
    private static string GetString(JsonElement item, string key, string fallback = "")
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    /// <summary>读取字符串数组属性。</summary>
    private static IEnumerable<string> GetStringArray(JsonElement item, string key)
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty)
            : [];

    /// <summary>读取配置中的整数。</summary>
    private static int GetConfigInt(JsonElement root, string[] path, int fallback)
    {
        var value = TryGetPath(root, path);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) ? number : fallback;
    }

    /// <summary>读取配置中的布尔值。</summary>
    private static bool GetConfigBool(JsonElement root, string[] path, bool fallback)
    {
        var value = TryGetPath(root, path);
        return value?.ValueKind == JsonValueKind.True ? true : value?.ValueKind == JsonValueKind.False ? false : fallback;
    }

    /// <summary>按路径读取嵌套 JSON 值。</summary>
    private static JsonElement? TryGetPath(JsonElement root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>输出经过 HTML escape 的文本。</summary>
    private static string H(string? value)
        => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>输出 Markdown pipeline 已生成的正文 HTML。只在明确的正文位置使用。</summary>
    private static string RawHtml(JsonElement item)
        => GetString(item, "html");

    /// <summary>格式化年月。</summary>
    private static string FormatYearMonth(DateTimeOffset? value)
        => value?.ToString("yyyy - MM", CultureInfo.InvariantCulture) ?? "undated";

    /// <summary>格式化详情页日期。</summary>
    private static string FormatDate(DateTimeOffset? value, string timeZone)
        => value is null ? string.Empty : $"{value.Value:yyyy-MM-dd} {timeZone}";

    /// <summary>默认 Theme 输入集合。</summary>
    private sealed record ThemeInputSet(JsonElement ThemeContext, JsonElement[] Posts, JsonElement[] Pages, JsonElement[] Works, JsonElement[] Notes, JsonElement[] Friends)
    {
        /// <summary>当前构建是否包含草稿。</summary>
        public bool IncludeDrafts => TryGetPath(ThemeContext, ["build", "includeDrafts"])?.ValueKind == JsonValueKind.True;
    }

    /// <summary>布局层常用站点信息。</summary>
    private sealed record SiteInfo(
        string Title,
        string DescriptionOrFallback,
        string Language,
        string BaseUrl,
        string AuthorName,
        string AuthorTimeZone,
        string AccentColor)
    {
        /// <summary>从 theme-context.json 的 data 节点创建站点信息。</summary>
        public static SiteInfo From(JsonElement context)
        {
            var title = TryGetPath(context, ["site", "title"])?.GetString() ?? "My Site";
            var description = TryGetPath(context, ["site", "description"])?.GetString();
            var language = TryGetPath(context, ["site", "language"])?.GetString() ?? "zh-CN";
            var baseUrl = TryGetPath(context, ["site", "baseUrl"])?.GetString() ?? "/";
            var author = TryGetPath(context, ["author", "displayName"])?.GetString()
                ?? TryGetPath(context, ["author", "name"])?.GetString()
                ?? "Anonymous";
            var timeZone = TryGetPath(context, ["author", "timeZone"])?.GetString()
                ?? TryGetPath(context, ["site", "timeZone"])?.GetString()
                ?? "UTC";
            var accent = TryGetPath(context, ["theme", "config", "visual", "accentColor"])?.GetString() ?? "#E85D3A";
            return new SiteInfo(title, string.IsNullOrWhiteSpace(description) ? $"A personal site by {author}." : description!, language, baseUrl, author, timeZone, accent);
        }
    }
}
