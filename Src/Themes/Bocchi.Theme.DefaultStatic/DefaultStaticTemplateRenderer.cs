using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Bocchi.GeneratorContract;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>内置 fluid-static renderer，负责把 Theme Contract 输入转成 Fluid 模板模型并输出静态文件。</summary>
public sealed class DefaultStaticTemplateRenderer
{
    /// <summary>默认 accent，配置值不合法时使用它避免 CSS 注入。</summary>
    private const string DefaultAccentColor = "#E85D3A";

    /// <summary>首页配置文案注入浏览器端 i18n JSON 时使用的虚拟 key。</summary>
    private const string HomeHeroTitleClientKey = "theme.config.home.heroTitle";

    /// <summary>首页副标题配置文案注入浏览器端 i18n JSON 时使用的虚拟 key。</summary>
    private const string HomeHeroSubtitleClientKey = "theme.config.home.heroSubtitle";

    /// <summary>首页 tag 配置文案注入浏览器端 i18n JSON 时使用的虚拟 key 前缀。</summary>
    private const string HomeTagClientKeyPrefix = "theme.config.home.tag.";

    /// <summary>需要随静态站点部署位置搬移的 HTML URL 属性。</summary>
    private static readonly Regex InternalUrlAttributeRegex = new(
        @"(?<prefix>\b(?:href|src|poster)\s*=\s*)(?<quote>[""'])(?<url>/(?!/)[^""']*)(?<suffix>\k<quote>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>浏览器端语言切换需要同步的 Theme chrome 文案 key。</summary>
    private static readonly string[] ClientI18nKeys =
    [
        "menu.home",
        "menu.posts",
        "menu.works",
        "menu.notes",
        "menu.friends",
        "theme.defaultStatic.homeSelectedWriting",
        "theme.defaultStatic.homeSelectedWork",
        "theme.defaultStatic.homeRecentNotes",
        "theme.defaultStatic.all",
        "theme.defaultStatic.postsDescription",
        "theme.defaultStatic.worksDescription",
        "theme.defaultStatic.notesDescription",
        "theme.defaultStatic.friendsDescription",
        "theme.defaultStatic.emptyList",
        "theme.defaultStatic.colophonBuiltWith",
        "theme.defaultStatic.linkLabel",
        "theme.defaultStatic.pageLabel",
        "theme.defaultStatic.articleBackPrefix",
        "theme.defaultStatic.notFoundDescription",
        "theme.defaultStatic.toggleAppearance",
        "theme.defaultStatic.openMenu",
        "theme.defaultStatic.languageLabel",
        "theme.defaultStatic.appearanceLabel",
        "theme.defaultStatic.appearanceAuto",
        "theme.defaultStatic.appearanceLight",
        "theme.defaultStatic.appearanceDark",
        "common.previous",
        "common.next",
        "common.backHome",
    ];

    /// <summary>执行一次默认 Theme 渲染。</summary>
    public static async Task RenderAsync(DefaultStaticRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = await ReadInputAsync(request.InputDirectory, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(request.OutputDirectory);

        var text = DefaultStaticThemeText.From(input.ThemeContext);
        var configTextFormats = await ReadConfigTextFormatsAsync(request.ThemeRoot, input.ThemeContext, cancellationToken).ConfigureAwait(false);
        var site = SiteInfo.From(input.ThemeContext, text.CurrentLanguage) with { Navigation = input.Navigation };
        var visiblePosts = FilterVisible(input.Posts, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visiblePages = FilterVisible(input.Pages, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();
        var visibleWorks = FilterVisible(input.Works, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleNotes = FilterVisible(input.Notes, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleFriends = FilterVisible(input.Friends, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();

        await WriteAssetsAsync(request, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "index.html", await RenderHomeAsync(request, site, text, input, configTextFormats, visiblePosts, visibleWorks, visibleNotes, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "posts/index.html", await RenderPostListAsync(request, site, text, visiblePosts, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePostDetailsAsync(request, site, text, visiblePosts, cancellationToken).ConfigureAwait(false);
        await WritePostCategoryPagesAsync(request, site, text, visiblePosts, input.PostCategories, cancellationToken).ConfigureAwait(false);
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
            ReadNavigationItems(await ReadDataAsync(inputDirectory, "navigation.json", cancellationToken).ConfigureAwait(false)),
            await ReadArrayAsync(inputDirectory, "post-categories.json", cancellationToken).ConfigureAwait(false),
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

    /// <summary>读取 Theme schema 中声明的可控文本格式；缺失或未知格式都会回退为 plain。</summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReadConfigTextFormatsAsync(
        string themeRoot,
        JsonElement context,
        CancellationToken cancellationToken)
    {
        var schema = await TryReadConfigSchemaAsync(themeRoot, context, cancellationToken).ConfigureAwait(false);
        if (schema is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(schema);
        if (!document.RootElement.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups.EnumerateArray().Where(group => group.ValueKind == JsonValueKind.Object))
        {
            if (!group.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var field in fields.EnumerateArray().Where(field => field.ValueKind == JsonValueKind.Object))
            {
                var key = GetString(field, "key");
                var type = GetString(field, "type");
                var format = DefaultStaticInlineTextRenderer.NormalizeFormat(GetString(field, "textFormat"));
                if (!string.IsNullOrWhiteSpace(key) &&
                    IsTextFormatEligibleField(type) &&
                    DefaultStaticInlineTextRenderer.IsInlineColorFormat(format))
                {
                    result[key.Trim()] = format;
                }
            }
        }

        return result;
    }

    /// <summary>读取当前 Theme 的 schema；内置默认 Theme 在运行实例缺文件时可回退到 embedded resource。</summary>
    private static async Task<string?> TryReadConfigSchemaAsync(
        string themeRoot,
        JsonElement context,
        CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(themeRoot, "config-schema.json");
        if (File.Exists(schemaPath))
        {
            return await File.ReadAllTextAsync(schemaPath, cancellationToken).ConfigureAwait(false);
        }

        var themeId = TryGetPath(context, ["theme", "id"])?.GetString();
        return string.Equals(themeId, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal)
            ? await DefaultStaticThemeResources.TryReadTextAsync("config-schema.json", cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <summary>判断字段类型是否允许声明 inline 文本格式。</summary>
    private static bool IsTextFormatEligibleField(string type)
        => string.Equals(type, "string", StringComparison.Ordinal)
            || string.Equals(type, "localizedText", StringComparison.Ordinal);

    /// <summary>读取 navigation.json 的 items 数组；无菜单时返回空数组。</summary>
    private static JsonElement[] ReadNavigationItems(JsonElement navigationData)
    {
        if (navigationData.ValueKind != JsonValueKind.Object ||
            !navigationData.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    /// <summary>复制用户可覆盖资产；缺失时回退到内置资产。</summary>
    private static async Task WriteAssetsAsync(DefaultStaticRenderRequest request, CancellationToken cancellationToken)
    {
        var assetOutput = Path.Combine(request.OutputDirectory, "assets");
        Directory.CreateDirectory(assetOutput);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "favicon.svg", cancellationToken).ConfigureAwait(false);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "app.css", cancellationToken).ConfigureAwait(false);
        await CopyOrWriteAssetAsync(request.ThemeRoot, assetOutput, "app.js", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>优先复制工作区 Theme asset，保证用户修改 CSS/JS 后构建能生效。</summary>
    private static async Task CopyOrWriteAssetAsync(
        string themeRoot,
        string outputDirectory,
        string fileName,
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

        await DefaultStaticThemeResources.CopyToFileAsync($"assets/{fileName}", destination, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>写入一个相对 Theme 输出目录的 HTML 文件。</summary>
    private static async Task WritePageAsync(string outputDirectory, string relativePath, string html, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var relocatableHtml = RelativizeInternalHtmlUrls(html, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, relocatableHtml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 将渲染后的站点根相对 URL 改写为相对当前 HTML 文件的 URL，让同一份输出可以部署在域名根目录或任意二级路径。
    /// </summary>
    private static string RelativizeInternalHtmlUrls(string html, string outputRelativePath)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRelativePath);

        var pageDirectory = GetPageDirectory(outputRelativePath);
        return InternalUrlAttributeRegex.Replace(html, match =>
        {
            var url = match.Groups["url"].Value;
            return string.Concat(
                match.Groups["prefix"].Value,
                match.Groups["quote"].Value,
                ToPageRelativeUrl(pageDirectory, url),
                match.Groups["suffix"].Value);
        });
    }

    /// <summary>根据输出 HTML 路径推导浏览器解析相对 URL 时所在的页面目录。</summary>
    private static string GetPageDirectory(string outputRelativePath)
    {
        var normalized = outputRelativePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash < 0 ? "/" : "/" + normalized[..(lastSlash + 1)];
    }

    /// <summary>把 <paramref name="targetUrl"/> 从站点根相对 URL 转为相对 <paramref name="pageDirectory"/> 的 URL。</summary>
    private static string ToPageRelativeUrl(string pageDirectory, string targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl) || targetUrl[0] != '/' || targetUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return targetUrl;
        }

        var suffixStart = targetUrl.IndexOfAny(['?', '#']);
        var targetPath = suffixStart < 0 ? targetUrl : targetUrl[..suffixStart];
        var suffix = suffixStart < 0 ? string.Empty : targetUrl[suffixStart..];
        var currentSegments = SplitUrlPath(pageDirectory);
        var targetSegments = SplitUrlPath(targetPath);
        var common = 0;
        while (common < currentSegments.Length &&
               common < targetSegments.Length &&
               string.Equals(currentSegments[common], targetSegments[common], StringComparison.Ordinal))
        {
            common++;
        }

        var parts = Enumerable.Repeat("..", currentSegments.Length - common)
            .Concat(targetSegments.Skip(common))
            .ToArray();
        var relative = parts.Length == 0 ? "." : string.Join("/", parts);
        if (targetPath.EndsWith("/", StringComparison.Ordinal))
        {
            relative += "/";
        }

        return relative + suffix;
    }

    /// <summary>拆分 URL path；根路径会返回空数组，便于计算相对层级。</summary>
    private static string[] SplitUrlPath(string path)
        => path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>渲染首页。</summary>
    private static Task<string> RenderHomeAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        ThemeInputSet input,
        IReadOnlyDictionary<string, string> configTextFormats,
        JsonElement[] posts,
        JsonElement[] works,
        JsonElement[] notes,
        CancellationToken cancellationToken)
    {
        var featuredPosts = MapContentItems(Limit(posts, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredPosts"], 5)), site);
        var featuredWorks = MapContentItems(Limit(works, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredWorks"], 4)), site);
        var recentNotes = MapContentItems(Limit(notes, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "recentNotes"], 3)), site);
        var homeCopy = CreateHomeCopyModel(input.ThemeContext, text, configTextFormats);

        var model = CreatePageModel(site, text, text.Get("menu.home"), "/");
        model["home"] = homeCopy.TemplateModel;
        model["localization"] = CreateLocalizationModel(text, homeCopy.ClientText);
        model["featuredPosts"] = featuredPosts;
        model["hasFeaturedPosts"] = featuredPosts.Length > 0;
        model["featuredWorks"] = featuredWorks;
        model["hasFeaturedWorks"] = featuredWorks.Length > 0;
        model["recentNotes"] = recentNotes;
        model["hasRecentNotes"] = recentNotes.Length > 0;
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
        var model = CreateListingModel(site, text, text.Get("menu.posts"), "menu.posts", "/posts/", text.Get("theme.defaultStatic.postsDescription"), "theme.defaultStatic.postsDescription", "02", items, text.Get("theme.defaultStatic.emptyList"));
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
        var model = CreateListingModel(site, text, text.Get("menu.works"), "menu.works", "/works/", text.Get("theme.defaultStatic.worksDescription"), "theme.defaultStatic.worksDescription", "03", items, text.Get("theme.defaultStatic.emptyList"));
        model["featuredWork"] = items.FirstOrDefault();
        model["hasFeaturedWork"] = items.Length > 0;
        model["workItems"] = items.Skip(1).ToArray();
        model["hasWorkItems"] = items.Length > 1;
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
        var titleKey = year is null ? "menu.notes" : string.Empty;
        var model = CreateListingModel(site, text, title, titleKey, currentPath, text.Get("theme.defaultStatic.notesDescription"), "theme.defaultStatic.notesDescription", "04", MapContentItems(notes, site), text.Get("theme.defaultStatic.emptyList"));
        model["year"] = year;
        model["isAllNotes"] = year is null;
        model["years"] = notes.Select(note => GetString(note, "year"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value, StringComparer.Ordinal)
            .Select(value => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["year"] = value,
                ["url"] = $"/notes/{value}/",
                ["current"] = string.Equals(value, year, StringComparison.Ordinal),
            })
            .ToArray();
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
        var model = CreateListingModel(site, text, text.Get("menu.friends"), "menu.friends", "/friends/", text.Get("theme.defaultStatic.friendsDescription"), "theme.defaultStatic.friendsDescription", "05", items, text.Get("theme.defaultStatic.emptyList"));
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
        model["hero"] = CreateHeroModel("404", text.Get("theme.defaultStatic.notFoundDescription"), "404", string.Empty, "theme.defaultStatic.notFoundDescription");
        return DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "404", model, cancellationToken);
    }

    /// <summary>渲染文章详情页。</summary>
    private static async Task WritePostDetailsAsync(DefaultStaticRenderRequest request, SiteInfo site, DefaultStaticThemeText text, JsonElement[] posts, CancellationToken cancellationToken)
    {
        for (var i = 0; i < posts.Length; i++)
        {
            var post = posts[i];
            var body = await RenderArticleAsync(request, site, text, text.Get("menu.posts"), "/posts/", post, Previous(posts, i), Next(posts, i), cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetContentUrl(post)), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>递归渲染 Post Category 列表页。</summary>
    private static async Task WritePostCategoryPagesAsync(
        DefaultStaticRenderRequest request,
        SiteInfo site,
        DefaultStaticThemeText text,
        JsonElement[] posts,
        JsonElement[] categories,
        CancellationToken cancellationToken)
    {
        foreach (var category in categories)
        {
            var slug = GetString(category, "slug");
            var categoryPosts = posts
                .Where(post => string.Equals(GetString(post, "categorySlug"), slug, StringComparison.Ordinal))
                .ToArray();
            var title = GetString(category, "name", slug);
            var currentPath = GetContentUrl(category);
            var model = CreateListingModel(
                site,
                text,
                title,
                string.Empty,
                currentPath,
                $"{text.Get("menu.posts")} / {title}",
                string.Empty,
                "02",
                MapContentItems(categoryPosts, site),
                text.Get("theme.defaultStatic.emptyList"));
            await WritePageAsync(
                request.OutputDirectory,
                ToOutputPath(currentPath),
                await DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, "posts", model, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            if (category.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                await WritePostCategoryPagesAsync(
                    request,
                    site,
                    text,
                    posts,
                    children.EnumerateArray().Select(child => child.Clone()).ToArray(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>渲染独立页面。</summary>
    private static async Task WriteStandalonePagesAsync(DefaultStaticRenderRequest request, SiteInfo site, DefaultStaticThemeText text, JsonElement[] pages, CancellationToken cancellationToken)
    {
        foreach (var page in pages)
        {
            var title = GetTitle(page);
            var model = CreatePageModel(site, text, title, GetContentUrl(page));
            model["item"] = MapContentItem(page, site);
            var template = await ResolveStandalonePageTemplateAsync(request.ThemeRoot, GetString(page, "template", "normal"), cancellationToken)
                .ConfigureAwait(false);
            var body = await DefaultStaticFluidRenderer.RenderPageAsync(request.ThemeRoot, template, model, cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetContentUrl(page)), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染作品详情页。</summary>
    private static async Task WriteWorkDetailsAsync(DefaultStaticRenderRequest request, SiteInfo site, DefaultStaticThemeText text, JsonElement[] works, CancellationToken cancellationToken)
    {
        for (var i = 0; i < works.Length; i++)
        {
            var work = works[i];
            var body = await RenderArticleAsync(request, site, text, text.Get("menu.works"), "/works/", work, Previous(works, i), Next(works, i), cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetContentUrl(work)), body, cancellationToken).ConfigureAwait(false);
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
        var model = CreatePageModel(site, text, title, GetContentUrl(item));
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

    /// <summary>按 Page template 选择独立页模板；专用模板缺失或名称不安全时回退到 standalone-page。</summary>
    private static async Task<string> ResolveStandalonePageTemplateAsync(
        string themeRoot,
        string template,
        CancellationToken cancellationToken)
    {
        if (string.Equals(template, "normal", StringComparison.Ordinal) || !IsSafeTemplateName(template))
        {
            return "standalone-page";
        }

        var candidate = $"standalone-page-{template}";
        return await DefaultStaticFluidRenderer.PageTemplateExistsAsync(themeRoot, candidate, cancellationToken).ConfigureAwait(false)
            ? candidate
            : "standalone-page";
    }

    private static bool IsSafeTemplateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    }

    /// <summary>创建列表页通用模板模型。</summary>
    private static Dictionary<string, object?> CreateListingModel(
        SiteInfo site,
        DefaultStaticThemeText text,
        string title,
        string titleKey,
        string currentPath,
        string description,
        string descriptionKey,
        string number,
        Dictionary<string, object?>[] items,
        string emptyText)
    {
        var model = CreatePageModel(site, text, title, currentPath);
        model["hero"] = CreateHeroModel(title, description, number, titleKey, descriptionKey);
        model["items"] = items;
        model["hasItems"] = items.Length > 0;
        model["itemCount"] = items.Length;
        model["emptyText"] = emptyText;
        model["emptyTextKey"] = "theme.defaultStatic.emptyList";
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
            ["navigation"] = CreateNavigationModel(currentPath, site.Navigation),
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

    /// <summary>创建顶栏和移动端导航使用的链接模型，保留 Menu tree 的嵌套结构。</summary>
    private static Dictionary<string, object?>[] CreateNavigationModel(string currentPath, JsonElement[] navigation)
        => navigation.Select(item => CreateNavigationItem(item, currentPath)).ToArray();

    /// <summary>创建单个导航链接模型并递归标记当前页。</summary>
    private static Dictionary<string, object?> CreateNavigationItem(JsonElement item, string currentPath)
    {
        var href = GetString(item, "href");
        var hasHref = !string.IsNullOrWhiteSpace(href);
        var children = item.TryGetProperty("children", out var childArray) && childArray.ValueKind == JsonValueKind.Array
            ? childArray.EnumerateArray().Select(child => CreateNavigationItem(child, currentPath)).ToArray()
            : [];
        var current = (hasHref && (string.Equals(href, currentPath, StringComparison.Ordinal) ||
            href != "/" && currentPath.StartsWith(href, StringComparison.Ordinal))) ||
            children.Any(child => child.TryGetValue("current", out var childCurrent) && childCurrent is true);
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["href"] = hasHref ? href : null,
            ["hasHref"] = hasHref,
            ["i18nKey"] = ReadNavigationI18nKey(item),
            ["label"] = GetString(item, "label"),
            ["current"] = current,
            ["children"] = children,
            ["hasChildren"] = children.Length > 0,
        };
        model["childrenHtml"] = RenderNavigationChildrenHtml(children, mobile: false);
        model["mobileChildrenHtml"] = RenderNavigationChildrenHtml(children, mobile: true);
        return model;
    }

    private static string? ReadNavigationI18nKey(JsonElement item)
    {
        var key = item.TryGetProperty("labelI18n", out var i18n) && i18n.ValueKind == JsonValueKind.Object
            ? GetString(i18n, "key")
            : string.Empty;
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>把嵌套 Menu 子树预渲染为 HTML，避免模板系统依赖递归 include。</summary>
    private static string RenderNavigationChildrenHtml(Dictionary<string, object?>[] children, bool mobile)
    {
        if (children.Length == 0)
        {
            return string.Empty;
        }

        var listClass = mobile ? "mobile-nav__children" : "nav__children";
        var itemClass = mobile ? "mobile-nav__item" : "nav__item";
        var builder = new StringBuilder();
        builder.Append("<ul class=\"").Append(listClass).Append("\">");
        foreach (var child in children)
        {
            var href = child.TryGetValue("href", out var hrefValue) ? hrefValue?.ToString() ?? string.Empty : string.Empty;
            var hasHref = !string.IsNullOrWhiteSpace(href);
            var label = child.TryGetValue("label", out var labelValue) ? labelValue?.ToString() ?? string.Empty : string.Empty;
            var i18nKey = child.TryGetValue("i18nKey", out var keyValue) ? keyValue?.ToString() ?? string.Empty : string.Empty;
            var current = child.TryGetValue("current", out var currentValue) && currentValue is true;
            var nestedHtml = child.TryGetValue(mobile ? "mobileChildrenHtml" : "childrenHtml", out var htmlValue)
                ? htmlValue?.ToString() ?? string.Empty
                : string.Empty;
            builder.Append("<li class=\"").Append(itemClass).Append("\">");
            if (hasHref)
            {
                builder.Append("<a href=\"")
                    .Append(WebUtility.HtmlEncode(href))
                    .Append('"');
                if (current)
                {
                    builder.Append(" aria-current=\"page\"");
                }

                AppendI18nAttribute(builder, i18nKey);
                builder.Append('>')
                    .Append(WebUtility.HtmlEncode(label))
                    .Append("</a>");
            }
            else
            {
                builder.Append("<span class=\"").Append(mobile ? "mobile-nav__label" : "nav__label").Append('"');
                AppendI18nAttribute(builder, i18nKey);
                builder.Append('>')
                    .Append(WebUtility.HtmlEncode(label))
                    .Append("</span>");
            }

            builder
                .Append(nestedHtml)
                .Append("</li>");
        }

        builder.Append("</ul>");
        return builder.ToString();
    }

    private static void AppendI18nAttribute(StringBuilder builder, string i18nKey)
    {
        if (!string.IsNullOrWhiteSpace(i18nKey))
        {
            builder.Append(" data-bocchi-i18n=\"").Append(WebUtility.HtmlEncode(i18nKey)).Append('"');
        }
    }

    /// <summary>创建页面 Hero 模型。</summary>
    private static Dictionary<string, object?> CreateHeroModel(string title, string description, string number, string? titleKey = null, string? descriptionKey = null)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = title,
            ["titleKey"] = titleKey ?? string.Empty,
            ["description"] = description,
            ["descriptionKey"] = descriptionKey ?? string.Empty,
            ["number"] = number,
        };
    }

    /// <summary>创建首页 Theme 配置文案模型，同时准备浏览器端语言切换需要的虚拟 i18n key。</summary>
    private static HomeCopyModel CreateHomeCopyModel(
        JsonElement context,
        DefaultStaticThemeText text,
        IReadOnlyDictionary<string, string> configTextFormats)
    {
        var titleValues = ReadLocalizedTextConfig(context, ["theme", "config", "home", "heroTitle"]);
        var subtitleValues = ReadLocalizedTextConfig(context, ["theme", "config", "home", "heroSubtitle"]);
        var tagValues = ReadLocalizedTextListConfig(context, ["theme", "config", "home", "tags"]);
        var title = ResolveLocalizedText(titleValues, text);
        var subtitle = ResolveLocalizedText(subtitleValues, text);
        var titleFormat = GetTextFormat(configTextFormats, "home.heroTitle");
        var subtitleFormat = GetTextFormat(configTextFormats, "home.heroSubtitle");
        var currentTags = ResolveLocalizedList(tagValues, text);
        var slotCount = Math.Max(currentTags.Length, tagValues.Values.Select(tagList => tagList.Length).DefaultIfEmpty(0).Max());
        var tagSlots = Enumerable.Range(0, slotCount)
            .Select(index => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = index < currentTags.Length ? currentTags[index] : string.Empty,
                ["i18nKey"] = HomeTagClientKeyPrefix + index.ToString(CultureInfo.InvariantCulture),
            })
            .ToArray();

        var clientText = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [HomeHeroTitleClientKey] = titleValues,
            [HomeHeroSubtitleClientKey] = subtitleValues,
        };
        for (var index = 0; index < slotCount; index++)
        {
            clientText[HomeTagClientKeyPrefix + index.ToString(CultureInfo.InvariantCulture)] =
                tagValues.ToDictionary(
                    pair => pair.Key,
                    pair => index < pair.Value.Length ? pair.Value[index] : string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }

        var templateModel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["heroTitle"] = title,
            ["heroTitleHtml"] = DefaultStaticInlineTextRenderer.Render(title, titleFormat),
            ["heroTitleKey"] = HomeHeroTitleClientKey,
            ["heroTitleFormat"] = ToClientTextFormat(titleFormat),
            ["heroSubtitle"] = subtitle,
            ["heroSubtitleHtml"] = DefaultStaticInlineTextRenderer.Render(subtitle, subtitleFormat),
            ["heroSubtitleKey"] = HomeHeroSubtitleClientKey,
            ["heroSubtitleFormat"] = ToClientTextFormat(subtitleFormat),
            ["tagSlots"] = tagSlots,
            ["hasTags"] = tagSlots.Length > 0,
        };
        return new HomeCopyModel(templateModel, clientText);
    }

    /// <summary>读取字段声明的文本格式；未声明时返回 plain。</summary>
    private static string GetTextFormat(IReadOnlyDictionary<string, string> formats, string key)
        => formats.TryGetValue(key, out var format) ? format : DefaultStaticInlineTextRenderer.PlainFormat;

    /// <summary>转换为前端 data attribute；plain 不输出属性，保持普通 i18n 路径。</summary>
    private static string ToClientTextFormat(string format)
        => DefaultStaticInlineTextRenderer.IsInlineColorFormat(format) ? DefaultStaticInlineTextRenderer.InlineColorFormat : string.Empty;

    /// <summary>读取 Theme 配置中的多语言文本对象；非对象值被当作当前语言的单值配置处理。</summary>
    private static Dictionary<string, string> ReadLocalizedTextConfig(JsonElement context, IReadOnlyList<string> path)
    {
        var value = TryGetPath(context, path);
        if (value is not { } element)
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            return string.IsNullOrWhiteSpace(raw) ? [] : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = raw.Trim(),
            };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return element.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            .ToDictionary(
                property => property.Name.Trim(),
                property => property.Value.GetString()!.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>读取 Theme 配置中的多语言文本列表对象；每个语言值必须是字符串数组。</summary>
    private static Dictionary<string, string[]> ReadLocalizedTextListConfig(JsonElement context, IReadOnlyList<string> path)
    {
        var value = TryGetPath(context, path);
        if (value is not { ValueKind: JsonValueKind.Object } element)
        {
            return [];
        }

        return element.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Array)
            .Select(property => new KeyValuePair<string, string[]>(
                property.Name.Trim(),
                property.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    .Select(item => item.GetString()!.Trim())
                    .ToArray()))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>按当前语言、站点主要语言、任意可用值的顺序解析多语言文本。</summary>
    private static string ResolveLocalizedText(Dictionary<string, string> values, DefaultStaticThemeText text)
    {
        foreach (var language in CreateConfigLanguageFallbacks(values.Keys, text))
        {
            if (values.TryGetValue(language, out var value))
            {
                return value;
            }
        }

        return values.Values.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>按当前语言、站点主要语言、任意可用值的顺序解析多语言列表。</summary>
    private static string[] ResolveLocalizedList(Dictionary<string, string[]> values, DefaultStaticThemeText text)
    {
        foreach (var language in CreateConfigLanguageFallbacks(values.Keys, text))
        {
            if (values.TryGetValue(language, out var value))
            {
                return value;
            }
        }

        return values.Values.FirstOrDefault() ?? [];
    }

    /// <summary>为 Theme 配置多语言值创建回退顺序，额外保留空语言 key 兼容单值配置。</summary>
    private static IEnumerable<string> CreateConfigLanguageFallbacks(IEnumerable<string> availableLanguages, DefaultStaticThemeText text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in new[] { text.CurrentLanguage, text.PrimaryLanguage, string.Empty })
        {
            if (seen.Add(language))
            {
                yield return language;
            }
        }

        foreach (var language in text.EnabledLanguages.Select(language => language.Code).Concat(availableLanguages))
        {
            if (!string.IsNullOrWhiteSpace(language) && seen.Add(language))
            {
                yield return language;
            }
        }
    }

    /// <summary>创建模板中用短属性名访问的前台文案模型，避免 Fluid 解析带点号的 i18n key。</summary>
    private static Dictionary<string, object?> CreateTextModel(DefaultStaticThemeText text)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
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
            ["languageLabel"] = text.Get("theme.defaultStatic.languageLabel"),
            ["appearanceLabel"] = text.Get("theme.defaultStatic.appearanceLabel"),
            ["appearanceAuto"] = text.Get("theme.defaultStatic.appearanceAuto"),
            ["appearanceLight"] = text.Get("theme.defaultStatic.appearanceLight"),
            ["appearanceDark"] = text.Get("theme.defaultStatic.appearanceDark"),
            ["previous"] = text.Get("common.previous"),
            ["next"] = text.Get("common.next"),
            ["backHome"] = text.Get("common.backHome"),
        };
    }

    /// <summary>创建模板可访问的站点本地化模型；默认 Theme 目前只展示当前语言，路径切换留给内容多语言页生成。</summary>
    private static Dictionary<string, object?> CreateLocalizationModel(
        DefaultStaticThemeText text,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? extraText = null)
    {
        var currentLanguage = text.EnabledLanguages.FirstOrDefault(
            language => string.Equals(language.Code, text.CurrentLanguage, StringComparison.OrdinalIgnoreCase));

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["currentLanguage"] = text.CurrentLanguage,
            ["currentLanguageName"] = currentLanguage?.NativeName ?? currentLanguage?.EnglishName ?? text.CurrentLanguage,
            ["primaryLanguage"] = text.PrimaryLanguage,
            ["textJson"] = text.BuildClientJson(ClientI18nKeys, extraText),
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
        var links = MapLinkArray(item);
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["url"] = GetContentUrl(item),
            ["title"] = GetTitle(item),
            ["summary"] = GetSummary(item),
            ["html"] = GetString(item, "html"),
            ["year"] = GetString(item, "year"),
            ["status"] = GetString(item, "status"),
            ["category"] = GetString(item, "category"),
            ["categorySlug"] = GetString(item, "categorySlug"),
            ["categoryUrl"] = string.IsNullOrWhiteSpace(GetString(item, "categorySlug"))
                ? string.Empty
                : $"/posts/categories/{GetString(item, "categorySlug")}/",
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
            ["links"] = links,
            ["hasLinks"] = links.Length > 0,
        };
    }

    /// <summary>把作品链接数组映射成模板可访问的字典数组。</summary>
    private static Dictionary<string, object?>[] MapLinkArray(JsonElement item)
    {
        if (!item.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return links.EnumerateArray()
            .Where(link => link.ValueKind == JsonValueKind.Object)
            .Select(link => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = GetString(link, "label", GetString(link, "url")),
                ["url"] = GetString(link, "url"),
            })
            .Where(link => !string.IsNullOrWhiteSpace(link["url"]?.ToString()))
            .ToArray();
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

    /// <summary>读取内容的 canonical 站点根相对 URL；优先使用新的 siteRelativeUrl 字段。</summary>
    private static string GetContentUrl(JsonElement item)
    {
        var siteRelativeUrl = GetString(item, "siteRelativeUrl");
        return string.IsNullOrWhiteSpace(siteRelativeUrl) ? GetString(item, "url") : siteRelativeUrl;
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
    private sealed record ThemeInputSet(
        JsonElement ThemeContext,
        JsonElement[] Navigation,
        JsonElement[] PostCategories,
        JsonElement[] Posts,
        JsonElement[] Pages,
        JsonElement[] Works,
        JsonElement[] Notes,
        JsonElement[] Friends)
    {
        /// <summary>当前构建是否包含草稿。</summary>
        public bool IncludeDrafts => TryGetPath(ThemeContext, ["build", "includeDrafts"])?.ValueKind == JsonValueKind.True;
    }

    /// <summary>首页配置文案的模板模型与浏览器端 i18n 扩展数据。</summary>
    private sealed record HomeCopyModel(
        Dictionary<string, object?> TemplateModel,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClientText);

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

        /// <summary>前台 primary menu tree。</summary>
        public JsonElement[] Navigation { get; init; } = [];

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
