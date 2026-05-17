using System.Globalization;
using System.Text;
using System.Text.Json;

using Bocchi.GeneratorContract;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>内置默认静态 Theme renderer，负责把 Theme Contract 输入转成 Fluid 模板模型并输出静态文件。</summary>
public sealed class DefaultStaticTemplateRenderer
{
    /// <summary>默认 accent，配置值不合法时使用它避免 CSS 注入。</summary>
    private const string DefaultAccentColor = "#E85D3A";

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

        var text = DefaultStaticThemeText.From(input.ThemeContext);
        var site = SiteInfo.From(input.ThemeContext, text.CurrentLanguage);
        var visiblePosts = FilterVisible(input.Posts, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visiblePages = FilterVisible(input.Pages, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();
        var visibleWorks = FilterVisible(input.Works, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleNotes = FilterVisible(input.Notes, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleFriends = FilterVisible(input.Friends, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();

        await WriteAssetsAsync(request, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "index.html", await RenderHomeAsync(request, site, text, input, visiblePosts, visibleWorks, visibleNotes, visibleFriends, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "posts/index.html", await RenderPostListAsync(request, site, text, visiblePosts, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePostDetailsAsync(request, site, text, visiblePosts, cancellationToken).ConfigureAwait(false);
        await WriteStandalonePagesAsync(request, site, text, visiblePages, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "works/index.html", await RenderWorkListAsync(request, site, text, visibleWorks, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WriteWorkDetailsAsync(request, site, text, visibleWorks, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "notes/index.html", await RenderNotesAsync(request, site, text, visibleNotes, null, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WriteNoteYearPagesAsync(request, site, text, visibleNotes, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "friends/index.html", await RenderFriendsAsync(request, site, text, visibleFriends, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "404.html", await RenderNotFoundAsync(request, site, text, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
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
    private static Task<string> RenderHomeAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        ThemeInputSet input,
        JsonElement[] posts,
        JsonElement[] works,
        JsonElement[] notes,
        JsonElement[] friends,
        CancellationToken cancellationToken)
    {
        var featuredPosts = MapContentItems(Limit(posts, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredPosts"], 5)), site);
        var featuredWorks = MapContentItems(Limit(works, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredWorks"], 4)), site);
        var recentNotes = MapContentItems(Limit(notes, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "recentNotes"], 3)), site);
        var friendLinks = MapFriendItems(Limit(friends, 6));

        var model = CreatePageModel(site, text, text.Get("menu.home"), "/");
        model["featuredPosts"] = featuredPosts;
        model["hasFeaturedPosts"] = featuredPosts.Length > 0;
        model["featuredWorks"] = featuredWorks;
        model["hasFeaturedWorks"] = featuredWorks.Length > 0;
        model["recentNotes"] = recentNotes;
        model["hasRecentNotes"] = recentNotes.Length > 0;
        model["friends"] = friendLinks;
        model["hasFriends"] = friendLinks.Length > 0;
        model["showFriends"] = GetConfigBool(input.ThemeContext, ["theme", "config", "home", "showFriends"], true);
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "index", model, cancellationToken);
    }

    /// <summary>渲染文章列表页。</summary>
    private static Task<string> RenderPostListAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        JsonElement[] posts,
        CancellationToken cancellationToken)
    {
        var items = MapContentItems(posts, site);
        var model = CreateListingModel(site, text, text.Get("menu.posts"), "/posts/", text.Get("theme.defaultStatic.postsDescription"), "02", items, text.Get("theme.defaultStatic.emptyList"));
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "posts", model, cancellationToken);
    }

    /// <summary>渲染作品列表页。</summary>
    private static Task<string> RenderWorkListAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        JsonElement[] works,
        CancellationToken cancellationToken)
    {
        var items = MapContentItems(works, site);
        var model = CreateListingModel(site, text, text.Get("menu.works"), "/works/", text.Get("theme.defaultStatic.worksDescription"), "03", items, text.Get("theme.defaultStatic.emptyList"));
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "works", model, cancellationToken);
    }

    /// <summary>渲染短文总览或年份页。</summary>
    private static Task<string> RenderNotesAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        JsonElement[] notes,
        string? year,
        CancellationToken cancellationToken)
    {
        var notesTitle = text.Get("menu.notes");
        var title = year is null ? notesTitle : $"{notesTitle} {year}";
        var currentPath = year is null ? "/notes/" : $"/notes/{year}/";
        var model = CreateListingModel(site, text, title, currentPath, text.Get("theme.defaultStatic.notesDescription"), "04", MapContentItems(notes, site), text.Get("theme.defaultStatic.emptyList"));
        model["year"] = year;
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "notes", model, cancellationToken);
    }

    /// <summary>渲染友链页。</summary>
    private static Task<string> RenderFriendsAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        JsonElement[] friends,
        CancellationToken cancellationToken)
    {
        var items = MapFriendItems(friends);
        var model = CreateListingModel(site, text, text.Get("menu.friends"), "/friends/", text.Get("theme.defaultStatic.friendsDescription"), "05", items, text.Get("theme.defaultStatic.emptyList"));
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "friends", model, cancellationToken);
    }

    /// <summary>渲染 404 页面。</summary>
    private static Task<string> RenderNotFoundAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        CancellationToken cancellationToken)
    {
        var model = CreatePageModel(site, text, "404", string.Empty);
        model["hero"] = CreateHeroModel("404", text.Get("theme.defaultStatic.notFoundDescription"), "404");
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "404", model, cancellationToken);
    }

    /// <summary>渲染文章详情页。</summary>
    private static async Task WritePostDetailsAsync(DefaultStaticRenderRequest request, SiteInfo site, DefaultStaticThemeText text, JsonElement[] posts, CancellationToken cancellationToken)
    {
        for (var i = 0; i < posts.Length; i++)
        {
            var post = posts[i];
            var body = await RenderArticleAsync(request, site, text, text.Get("menu.posts"), "/posts/", post, Previous(posts, i), Next(posts, i), cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetString(post, "url")), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染独立页面。</summary>
    private static async Task WriteStandalonePagesAsync(DefaultStaticRenderRequest request, SiteInfo site, DefaultStaticThemeText text, JsonElement[] pages, CancellationToken cancellationToken)
    {
        foreach (var page in pages)
        {
            var title = GetTitle(page);
            var model = CreatePageModel(site, text, title, GetString(page, "url"));
            model["item"] = MapContentItem(page, site);
            var body = await DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "standalone-page", model, cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetString(page, "url")), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染作品详情页。</summary>
    private static async Task WriteWorkDetailsAsync(DefaultStaticRenderRequest request, SiteInfo site, DefaultStaticThemeText text, JsonElement[] works, CancellationToken cancellationToken)
    {
        for (var i = 0; i < works.Length; i++)
        {
            var work = works[i];
            var body = await RenderArticleAsync(request, site, text, text.Get("menu.works"), "/works/", work, Previous(works, i), Next(works, i), cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetString(work, "url")), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>按年份渲染短文页。</summary>
    private static async Task WriteNoteYearPagesAsync(DefaultStaticRenderRequest request, SiteInfo site, DefaultStaticThemeText text, JsonElement[] notes, CancellationToken cancellationToken)
    {
        foreach (var group in notes.GroupBy(note => GetString(note, "year")).Where(group => !string.IsNullOrWhiteSpace(group.Key)))
        {
            await WritePageAsync(
                request.OutputDirectory,
                $"notes/{group.Key}/index.html",
                await RenderNotesAsync(request, site, text, group.ToArray(), group.Key, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染文章或作品详情。</summary>
    private static Task<string> RenderArticleAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        string sectionName,
        string sectionUrl,
        JsonElement item,
        JsonElement? previous,
        JsonElement? next,
        CancellationToken cancellationToken)
    {
        var title = GetTitle(item);
        var model = CreatePageModel(site, text, title, GetString(item, "url"));
        model["section"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = sectionName,
            ["url"] = sectionUrl,
        };
        model["item"] = MapContentItem(item, site);
        model["previous"] = previous is null ? null : MapContentItem(previous.Value, site);
        model["hasPrevious"] = previous is not null;
        model["next"] = next is null ? null : MapContentItem(next.Value, site);
        model["hasNext"] = next is not null;
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "article", model, cancellationToken);
    }

    /// <summary>创建列表页通用模板模型。</summary>
    private static Dictionary<string, object?> CreateListingModel(
        SiteInfo site,
        DefaultStaticThemeText text,
        string title,
        string currentPath,
        string description,
        string number,
        Dictionary<string, object?>[] items,
        string emptyText)
    {
        var model = CreatePageModel(site, text, title, currentPath);
        model["hero"] = CreateHeroModel(title, description, number);
        model["items"] = items;
        model["hasItems"] = items.Length > 0;
        model["emptyText"] = emptyText;
        return model;
    }

    /// <summary>创建所有页面共享的模板模型。</summary>
    private static Dictionary<string, object?> CreatePageModel(SiteInfo site, DefaultStaticThemeText text, string title, string currentPath)
    {
        var fullTitle = string.Equals(title, text.Get("menu.home"), StringComparison.Ordinal) ? site.DefaultTitle : $"{title} · {site.Title}";
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["site"] = CreateSiteModel(site),
            ["text"] = CreateTextModel(text),
            ["localization"] = CreateLocalizationModel(text),
            ["page"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = title,
                ["fullTitle"] = fullTitle,
                ["currentPath"] = currentPath,
            },
            ["navigation"] = CreateNavigationModel(currentPath, text),
        };
    }

    /// <summary>创建布局层使用的站点模型。</summary>
    private static Dictionary<string, object?> CreateSiteModel(SiteInfo site)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = site.Title,
            ["defaultTitle"] = site.DefaultTitle,
            ["description"] = site.DescriptionOrFallback,
            ["language"] = site.Language,
            ["baseUrl"] = site.BaseUrl,
            ["copyrightNotice"] = site.CopyrightNotice,
            ["authorName"] = site.AuthorName,
            ["authorTimeZone"] = site.AuthorTimeZone,
            ["accentColor"] = site.AccentColor,
            ["generatedYear"] = site.GeneratedYear,
        };
    }

    /// <summary>创建顶栏和移动端导航使用的链接模型。</summary>
    private static Dictionary<string, object?>[] CreateNavigationModel(string currentPath, DefaultStaticThemeText text)
    {
        return new[]
        {
            CreateNavigationItem("/", text.Get("menu.home"), currentPath),
            CreateNavigationItem("/posts/", text.Get("menu.posts"), currentPath),
            CreateNavigationItem("/works/", text.Get("menu.works"), currentPath),
            CreateNavigationItem("/notes/", text.Get("menu.notes"), currentPath),
            CreateNavigationItem("/friends/", text.Get("menu.friends"), currentPath),
        };
    }

    /// <summary>创建单个导航链接模型并标记当前页。</summary>
    private static Dictionary<string, object?> CreateNavigationItem(string href, string label, string currentPath)
    {
        var current = string.Equals(href, currentPath, StringComparison.Ordinal) ||
            href != "/" && currentPath.StartsWith(href, StringComparison.Ordinal);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["href"] = href,
            ["label"] = label,
            ["current"] = current,
        };
    }

    /// <summary>创建页面 Hero 模型。</summary>
    private static Dictionary<string, object?> CreateHeroModel(string title, string description, string number)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = title,
            ["description"] = description,
            ["number"] = number,
        };
    }

    /// <summary>创建模板中用短属性名访问的前台文案模型，避免 Fluid 解析带点号的 i18n key。</summary>
    private static Dictionary<string, object?> CreateTextModel(DefaultStaticThemeText text)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["homeHeroAccent"] = text.Get("theme.defaultStatic.homeHeroAccent"),
            ["homeHeroRest"] = text.Get("theme.defaultStatic.homeHeroRest"),
            ["homeSelectedWriting"] = text.Get("theme.defaultStatic.homeSelectedWriting"),
            ["homeSelectedWork"] = text.Get("theme.defaultStatic.homeSelectedWork"),
            ["homeRecentNotes"] = text.Get("theme.defaultStatic.homeRecentNotes"),
            ["homeFriends"] = text.Get("menu.friends"),
            ["all"] = text.Get("theme.defaultStatic.all"),
            ["emptyList"] = text.Get("theme.defaultStatic.emptyList"),
            ["colophonBuiltWith"] = text.Get("theme.defaultStatic.colophonBuiltWith"),
            ["linkLabel"] = text.Get("theme.defaultStatic.linkLabel"),
            ["pageLabel"] = text.Get("theme.defaultStatic.pageLabel"),
            ["articleBackPrefix"] = text.Get("theme.defaultStatic.articleBackPrefix"),
            ["toggleAppearance"] = text.Get("theme.defaultStatic.toggleAppearance"),
            ["openMenu"] = text.Get("theme.defaultStatic.openMenu"),
            ["previous"] = text.Get("common.previous"),
            ["next"] = text.Get("common.next"),
            ["backHome"] = text.Get("common.backHome"),
        };
    }

    /// <summary>创建模板可访问的站点本地化模型；默认 Theme 目前只展示当前语言，路径切换留给内容多语言页生成。</summary>
    private static Dictionary<string, object?> CreateLocalizationModel(DefaultStaticThemeText text)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["currentLanguage"] = text.CurrentLanguage,
            ["primaryLanguage"] = text.PrimaryLanguage,
            ["languages"] = text.EnabledLanguages
                .Select(language => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = language.Code,
                    ["nativeName"] = language.NativeName,
                    ["englishName"] = language.EnglishName,
                })
                .ToArray(),
            ["hasMultipleLanguages"] = text.EnabledLanguages.Length > 1,
        };
    }

    /// <summary>把内容输入映射成模板可访问的字典数组。</summary>
    private static Dictionary<string, object?>[] MapContentItems(IEnumerable<JsonElement> items, SiteInfo site)
        => items.Select(item => MapContentItem(item, site)).ToArray();

    /// <summary>把文章、页面、作品或短文映射成模板模型。</summary>
    private static Dictionary<string, object?> MapContentItem(JsonElement item, SiteInfo site)
    {
        var date = GetContentDate(item);
        var tags = GetStringArray(item, "tags").ToArray();
        var stack = GetStringArray(item, "stack").ToArray();
        var cover = MapMediaReference(item, "cover");
        var media = MapMediaArray(item);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["url"] = GetString(item, "url"),
            ["title"] = GetTitle(item),
            ["summary"] = GetSummary(item),
            ["html"] = GetString(item, "html"),
            ["year"] = GetString(item, "year"),
            ["status"] = GetString(item, "status"),
            ["category"] = GetString(item, "category"),
            ["role"] = GetString(item, "role"),
            ["period"] = GetString(item, "period"),
            ["date"] = FormatDate(date, site.AuthorTimeZone),
            ["yearMonth"] = FormatYearMonth(date),
            ["isoDate"] = date?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ["displayDateTime"] = date?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "undated",
            ["meta"] = BuildRowMeta(item, tags),
            ["tags"] = tags,
            ["hasTags"] = tags.Length > 0,
            ["stack"] = stack,
            ["hasStack"] = stack.Length > 0,
            ["cover"] = cover,
            ["hasCover"] = cover is not null,
            ["media"] = media,
            ["hasMedia"] = media.Length > 0,
        };
    }

    /// <summary>把友链输入映射成模板可访问的字典数组。</summary>
    private static Dictionary<string, object?>[] MapFriendItems(IEnumerable<JsonElement> friends)
        => friends.Select(MapFriendItem).ToArray();

    /// <summary>把单条友链映射成模板模型。</summary>
    private static Dictionary<string, object?> MapFriendItem(JsonElement friend)
    {
        var avatar = MapMediaReference(friend, "avatar");
        var tags = GetStringArray(friend, "tags").ToArray();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["url"] = GetString(friend, "url"),
            ["title"] = GetTitle(friend),
            ["summary"] = GetSummary(friend),
            ["meta"] = GetSummary(friend),
            ["tags"] = tags,
            ["hasTags"] = tags.Length > 0,
            ["avatar"] = avatar,
            ["hasAvatar"] = avatar is not null,
        };
    }

    /// <summary>读取单个媒体引用对象。</summary>
    private static Dictionary<string, object?>? MapMediaReference(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var media) || media.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var path = GetString(media, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = path,
            ["alt"] = GetString(media, "alt"),
        };
    }

    /// <summary>读取内容正文引用的媒体数组。</summary>
    private static Dictionary<string, object?>[] MapMediaArray(JsonElement item)
    {
        if (!item.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return media.EnumerateArray()
            .Select(MapMediaReferenceValue)
            .Where(value => value is not null)
            .Cast<Dictionary<string, object?>>()
            .ToArray();
    }

    /// <summary>把媒体引用 JSON 对象映射成模板模型。</summary>
    private static Dictionary<string, object?>? MapMediaReferenceValue(JsonElement media)
    {
        if (media.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var path = GetString(media, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = path,
            ["alt"] = GetString(media, "alt"),
        };
    }

    /// <summary>为行式列表生成紧凑 meta 文本。</summary>
    private static string BuildRowMeta(JsonElement item, IReadOnlyList<string> tags)
    {
        var category = GetString(item, "category");
        if (!string.IsNullOrWhiteSpace(category))
        {
            return category;
        }

        var period = GetString(item, "period");
        if (!string.IsNullOrWhiteSpace(period))
        {
            return period;
        }

        return string.Join(" · ", tags.Take(2));
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

    /// <summary>格式化年月。</summary>
    private static string FormatYearMonth(DateTimeOffset? value)
        => value?.ToString("yyyy - MM", CultureInfo.InvariantCulture) ?? "undated";

    /// <summary>格式化详情页日期。</summary>
    private static string FormatDate(DateTimeOffset? value, string timeZone)
        => value is null ? string.Empty : $"{value.Value:yyyy-MM-dd} {timeZone}";

    /// <summary>只接受十六进制 CSS color，避免配置值逃逸到 style 上下文。</summary>
    private static string NormalizeAccentColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length is not (4 or 7))
        {
            return DefaultAccentColor;
        }

        return value.Skip(1).All(Uri.IsHexDigit) ? value : DefaultAccentColor;
    }

    /// <summary>默认 Theme 输入集合。</summary>
    private sealed record ThemeInputSet(JsonElement ThemeContext, JsonElement[] Posts, JsonElement[] Pages, JsonElement[] Works, JsonElement[] Notes, JsonElement[] Friends)
    {
        /// <summary>当前构建是否包含草稿。</summary>
        public bool IncludeDrafts => TryGetPath(ThemeContext, ["build", "includeDrafts"])?.ValueKind == JsonValueKind.True;
    }

    /// <summary>布局层常用站点信息。</summary>
    private sealed record SiteInfo
    {
        /// <summary>站点标题。</summary>
        public required string Title { get; init; }

        /// <summary>首页和无专用标题页面使用的默认标题。</summary>
        public required string DefaultTitle { get; init; }

        /// <summary>站点描述；缺失时会使用作者 fallback。</summary>
        public required string DescriptionOrFallback { get; init; }

        /// <summary>HTML lang 使用的语言代码。</summary>
        public required string Language { get; init; }

        /// <summary>站点基础 URL。</summary>
        public required string BaseUrl { get; init; }

        /// <summary>前台 footer 使用的版权文案。</summary>
        public required string CopyrightNotice { get; init; }

        /// <summary>作者展示名。</summary>
        public required string AuthorName { get; init; }

        /// <summary>作者时区。</summary>
        public required string AuthorTimeZone { get; init; }

        /// <summary>经过 CSS 安全归一化的 accent color。</summary>
        public required string AccentColor { get; init; }

        /// <summary>构建时间对应年份，用于 footer。</summary>
        public required int GeneratedYear { get; init; }

        /// <summary>从 theme-context.json 的 data 节点创建站点信息。</summary>
        public static SiteInfo From(JsonElement context, string currentLanguage)
        {
            var title = TryGetPath(context, ["site", "title"])?.GetString() ?? "My Site";
            var defaultTitle = TryGetPath(context, ["site", "defaultTitle"])?.GetString();
            var description = TryGetPath(context, ["site", "description"])?.GetString();
            var baseUrl = TryGetPath(context, ["site", "baseUrl"])?.GetString() ?? "/";
            var copyright = TryGetPath(context, ["site", "copyrightNotice"])?.GetString();
            var author = TryGetPath(context, ["author", "displayName"])?.GetString()
                ?? TryGetPath(context, ["author", "name"])?.GetString()
                ?? "Anonymous";
            var timeZone = TryGetPath(context, ["author", "timeZone"])?.GetString()
                ?? TryGetPath(context, ["site", "timeZone"])?.GetString()
                ?? "UTC";
            var accent = NormalizeAccentColor(TryGetPath(context, ["theme", "config", "visual", "accentColor"])?.GetString());
            var generatedAtRaw = TryGetPath(context, ["build", "generatedAt"])?.GetString();
            var generatedYear = DateTimeOffset.TryParse(generatedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var generatedAt)
                ? generatedAt.Year
                : DateTimeOffset.UtcNow.Year;

            return new SiteInfo
            {
                Title = title,
                DefaultTitle = string.IsNullOrWhiteSpace(defaultTitle) ? title : defaultTitle!,
                DescriptionOrFallback = string.IsNullOrWhiteSpace(description) ? $"A personal site by {author}." : description!,
                Language = string.IsNullOrWhiteSpace(currentLanguage) ? "zh-CN" : currentLanguage,
                BaseUrl = baseUrl,
                CopyrightNotice = string.IsNullOrWhiteSpace(copyright) ? $"{title} · {generatedYear}" : copyright!,
                AuthorName = author,
                AuthorTimeZone = timeZone,
                AccentColor = accent,
                GeneratedYear = generatedYear,
            };
        }
    }
}
