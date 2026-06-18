using System.Text.Json;

namespace Bocchi.Theme.FluidStatic;

/// <summary>内置 fluid-static renderer，负责把 Theme Contract 输入转成 Fluid 模板模型并输出静态文件。</summary>
public sealed partial class FluidStaticRenderer
{
    /// <summary>执行一次默认 Theme 渲染。</summary>
    public static async Task RenderAsync(FluidStaticRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        FluidStaticTemplateContract.EnsureRequiredTemplates(request.ThemeRoot);
        var input = await ReadInputAsync(request.InputDirectory, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(request.OutputDirectory);
        await FluidStaticRuntimeResources.CopyToAsync(request.OutputDirectory, cancellationToken).ConfigureAwait(false);

        var text = FluidStaticTextResolver.From(input.ThemeContext);
        var theme = CreateThemeModel(request.Manifest, input.ThemeContext);
        var configTextFormats = await ReadConfigTextFormatsAsync(request.ThemeRoot, input.ThemeContext, cancellationToken).ConfigureAwait(false);
        var site = SiteInfo.From(input.ThemeContext, text.CurrentLanguage) with { Navigation = input.Navigation };
        var visiblePosts = FilterVisible(input.Posts, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visiblePages = FilterVisible(input.Pages, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();
        var visibleWorks = FilterVisible(input.Works, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleNotes = FilterVisible(input.Notes, input.IncludeDrafts).OrderByDescending(GetContentDate).ToArray();
        var visibleFriends = FilterVisible(input.Friends, input.IncludeDrafts).OrderBy(GetOrder).ThenBy(GetTitle).ToArray();
        var listingPosts = SelectListingRepresentatives(visiblePosts, text.CurrentLanguage);
        var listingWorks = SelectListingRepresentatives(visibleWorks, text.CurrentLanguage);

        await WritePageAsync(request.OutputDirectory, "index.html", await RenderHomeAsync(request, site, text, theme, input, configTextFormats, listingPosts, visiblePosts, listingWorks, visibleWorks, visibleNotes, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "posts/index.html", await RenderPostListAsync(request, site, text, theme, listingPosts, visiblePosts, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePostDetailsAsync(request, site, text, theme, visiblePosts, cancellationToken).ConfigureAwait(false);
        await WritePostCategoryPagesAsync(request, site, text, theme, listingPosts, visiblePosts, input.PostCategories, cancellationToken).ConfigureAwait(false);
        await WriteStandalonePagesAsync(request, site, text, theme, visiblePages, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "works/index.html", await RenderWorkListAsync(request, site, text, theme, listingWorks, visibleWorks, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WriteWorkDetailsAsync(request, site, text, theme, visibleWorks, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "notes/index.html", await RenderNotesAsync(request, site, text, theme, visibleNotes, null, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WriteNoteYearPagesAsync(request, site, text, theme, visibleNotes, cancellationToken).ConfigureAwait(false);
        await WriteNoteDetailsAsync(request, site, text, theme, visibleNotes, cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "friends/index.html", await RenderFriendsAsync(request, site, text, theme, visibleFriends, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        await WritePageAsync(request.OutputDirectory, "404.html", await RenderNotFoundAsync(request, site, text, theme, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>渲染首页。</summary>
    private static Task<string> RenderHomeAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        ThemeInputSet input,
        IReadOnlyDictionary<string, string> configTextFormats,
        JsonElement[] posts,
        JsonElement[] allPosts,
        JsonElement[] works,
        JsonElement[] allWorks,
        JsonElement[] notes,
        CancellationToken cancellationToken)
    {
        var featuredPosts = MapListingContentItems(Limit(posts, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredPosts"], 5)), allPosts, site, "/");
        var featuredWorks = MapListingContentItems(Limit(works, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "featuredWorks"], 4)), allWorks, site, "/");
        var recentNotes = MapContentItems(Limit(notes, GetConfigInt(input.ThemeContext, ["theme", "config", "home", "recentNotes"], 3)), site);
        var homeCopy = CreateHomeCopyModel(input.ThemeContext, text, configTextFormats);

        var model = CreatePageModel(site, text, theme, text.Get("menu.home"), "/");
        model["home"] = homeCopy.TemplateModel;
        model["localization"] = CreateLocalizationModel(text, site.Navigation, homeCopy.ClientText);
        model["featuredPosts"] = featuredPosts;
        model["hasFeaturedPosts"] = featuredPosts.Length > 0;
        model["featuredWorks"] = featuredWorks;
        model["hasFeaturedWorks"] = featuredWorks.Length > 0;
        model["recentNotes"] = recentNotes;
        model["hasRecentNotes"] = recentNotes.Length > 0;
        return FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "index", model, text, cancellationToken);
    }

    /// <summary>渲染文章列表页。</summary>
    private static Task<string> RenderPostListAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        JsonElement[] posts,
        JsonElement[] allPosts,
        CancellationToken cancellationToken)
    {
        var items = MapListingContentItems(posts, allPosts, site, "/posts/");
        var descriptionKey = ThemeTextKey(theme, "postsDescription");
        var model = CreateListingModel(site, text, theme, text.Get("menu.posts"), "menu.posts", "/posts/", text.Get(descriptionKey), descriptionKey, "02", items, text.Get(ThemeTextKey(theme, "emptyList")));
        return FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "posts", model, text, cancellationToken);
    }

    /// <summary>渲染作品列表页。</summary>
    private static Task<string> RenderWorkListAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        JsonElement[] works,
        JsonElement[] allWorks,
        CancellationToken cancellationToken)
    {
        var items = MapListingContentItems(works, allWorks, site, "/works/");
        var descriptionKey = ThemeTextKey(theme, "worksDescription");
        var model = CreateListingModel(site, text, theme, text.Get("menu.works"), "menu.works", "/works/", text.Get(descriptionKey), descriptionKey, "03", items, text.Get(ThemeTextKey(theme, "emptyList")));
        model["featuredWork"] = items.FirstOrDefault();
        model["hasFeaturedWork"] = items.Length > 0;
        model["workItems"] = items.Skip(1).ToArray();
        model["hasWorkItems"] = items.Length > 1;
        return FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "works", model, text, cancellationToken);
    }

    /// <summary>渲染短文总览或年份页。</summary>
    private static Task<string> RenderNotesAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        JsonElement[] notes,
        string? year,
        CancellationToken cancellationToken)
    {
        var notesTitle = text.Get("menu.notes");
        var title = year is null ? notesTitle : $"{notesTitle} {year}";
        var currentPath = year is null ? "/notes/" : $"/notes/{year}/";
        var titleKey = year is null ? "menu.notes" : string.Empty;
        var descriptionKey = ThemeTextKey(theme, "notesDescription");
        var model = CreateListingModel(site, text, theme, title, titleKey, currentPath, text.Get(descriptionKey), descriptionKey, "04", MapContentItems(notes, site), text.Get(ThemeTextKey(theme, "emptyList")));
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
        return FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "notes", model, text, cancellationToken);
    }

    /// <summary>渲染友链页。</summary>
    private static Task<string> RenderFriendsAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        JsonElement[] friends,
        CancellationToken cancellationToken)
    {
        var items = MapFriendItems(friends);
        var descriptionKey = ThemeTextKey(theme, "friendsDescription");
        var model = CreateListingModel(site, text, theme, text.Get("menu.friends"), "menu.friends", "/friends/", text.Get(descriptionKey), descriptionKey, "05", items, text.Get(ThemeTextKey(theme, "emptyList")));
        return FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "friends", model, text, cancellationToken);
    }

    /// <summary>渲染 404 页面。</summary>
    private static Task<string> RenderNotFoundAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        CancellationToken cancellationToken)
    {
        var model = CreatePageModel(site, text, theme, "404", string.Empty);
        var descriptionKey = ThemeTextKey(theme, "notFoundDescription");
        model["hero"] = CreateHeroModel("404", text.Get(descriptionKey), "404", string.Empty, descriptionKey);
        return FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "404", model, text, cancellationToken);
    }

    /// <summary>渲染文章详情页。</summary>
    private static async Task WritePostDetailsAsync(FluidStaticRenderRequest request, SiteInfo site, FluidStaticTextResolver text, IReadOnlyDictionary<string, object?> theme, JsonElement[] posts, CancellationToken cancellationToken)
    {
        for (var i = 0; i < posts.Length; i++)
        {
            var post = posts[i];
            var neighbors = SelectListingRepresentatives(posts, GetString(post, "language", text.CurrentLanguage));
            var index = Array.FindIndex(neighbors, item => IsSameContentItem(item, post));
            var body = await RenderArticleAsync(
                request,
                site,
                text,
                theme,
                "menu.posts",
                "/posts/",
                post,
                index >= 0 ? Previous(neighbors, index) : null,
                index >= 0 ? Next(neighbors, index) : null,
                includeArticleTime: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetContentUrl(post)), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>递归渲染 Post Category 列表页。</summary>
    private static async Task WritePostCategoryPagesAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        JsonElement[] posts,
        JsonElement[] allPosts,
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
                theme,
                title,
                string.Empty,
                currentPath,
                $"{text.Get("menu.posts")} / {title}",
                string.Empty,
                "02",
                MapListingContentItems(categoryPosts, allPosts.Where(post => string.Equals(GetString(post, "categorySlug"), slug, StringComparison.Ordinal)), site, currentPath),
                text.Get(ThemeTextKey(theme, "emptyList")));
            await WritePageAsync(
                request.OutputDirectory,
                ToOutputPath(currentPath),
                await FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "posts", model, text, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            if (category.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                await WritePostCategoryPagesAsync(
                    request,
                    site,
                    text,
                    theme,
                    posts,
                    allPosts,
                    children.EnumerateArray().Select(child => child.Clone()).ToArray(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>渲染独立页面。</summary>
    private static async Task WriteStandalonePagesAsync(FluidStaticRenderRequest request, SiteInfo site, FluidStaticTextResolver text, IReadOnlyDictionary<string, object?> theme, JsonElement[] pages, CancellationToken cancellationToken)
    {
        foreach (var page in pages)
        {
            var pageText = text.WithCurrentLanguage(GetString(page, "language"));
            var title = GetTitle(page);
            var model = CreatePageModel(site, pageText, theme, title, GetContentUrl(page), page);
            model["item"] = MapContentItem(page, site, pageText);
            var template = await ResolveStandalonePageTemplateAsync(request.ThemeRoot, GetString(page, "template", "normal"), cancellationToken)
                .ConfigureAwait(false);
            var body = await FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, template, model, pageText, cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetContentUrl(page)), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染作品详情页。</summary>
    private static async Task WriteWorkDetailsAsync(FluidStaticRenderRequest request, SiteInfo site, FluidStaticTextResolver text, IReadOnlyDictionary<string, object?> theme, JsonElement[] works, CancellationToken cancellationToken)
    {
        for (var i = 0; i < works.Length; i++)
        {
            var work = works[i];
            var neighbors = SelectListingRepresentatives(works, GetString(work, "language", text.CurrentLanguage));
            var index = Array.FindIndex(neighbors, item => IsSameContentItem(item, work));
            var body = await RenderArticleAsync(
                request,
                site,
                text,
                theme,
                "menu.works",
                "/works/",
                work,
                index >= 0 ? Previous(neighbors, index) : null,
                index >= 0 ? Next(neighbors, index) : null,
                includeArticleTime: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetContentUrl(work)), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>按年份渲染短文页。</summary>
    private static async Task WriteNoteYearPagesAsync(FluidStaticRenderRequest request, SiteInfo site, FluidStaticTextResolver text, IReadOnlyDictionary<string, object?> theme, JsonElement[] notes, CancellationToken cancellationToken)
    {
        foreach (var group in notes.GroupBy(note => GetString(note, "year")).Where(group => !string.IsNullOrWhiteSpace(group.Key)))
        {
            await WritePageAsync(
                request.OutputDirectory,
                $"notes/{group.Key}/index.html",
                await RenderNotesAsync(request, site, text, theme, group.ToArray(), group.Key, cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染短文详情页，公开路径只使用稳定短 id。</summary>
    private static async Task WriteNoteDetailsAsync(FluidStaticRenderRequest request, SiteInfo site, FluidStaticTextResolver text, IReadOnlyDictionary<string, object?> theme, JsonElement[] notes, CancellationToken cancellationToken)
    {
        for (var i = 0; i < notes.Length; i++)
        {
            var note = notes[i];
            var body = await RenderArticleAsync(
                request,
                site,
                text,
                theme,
                "menu.notes",
                "/notes/",
                note,
                Previous(notes, i),
                Next(notes, i),
                includeArticleTime: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await WritePageAsync(request.OutputDirectory, ToOutputPath(GetContentUrl(note)), body, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>渲染文章或作品详情。</summary>
    private static Task<string> RenderArticleAsync(
        FluidStaticRenderRequest request,
        SiteInfo site,
        FluidStaticTextResolver text,
        IReadOnlyDictionary<string, object?> theme,
        string sectionKey,
        string sectionUrl,
        JsonElement item,
        JsonElement? previous,
        JsonElement? next,
        bool includeArticleTime,
        CancellationToken cancellationToken)
    {
        var pageText = text.WithCurrentLanguage(GetString(item, "language"));
        var title = GetTitle(item);
        var model = CreatePageModel(site, pageText, theme, title, GetContentUrl(item), item);
        model["section"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = pageText.Get(sectionKey),
            ["url"] = sectionUrl,
        };
        model["item"] = MapContentItem(item, site, pageText, includeArticleTime);
        model["previous"] = previous is null ? null : MapContentItem(previous.Value, site);
        model["hasPrevious"] = previous is not null;
        model["next"] = next is null ? null : MapContentItem(next.Value, site);
        model["hasNext"] = next is not null;
        return FluidStaticLiquidRenderer.RenderPageAsync(request.ThemeRoot, "article", model, pageText, cancellationToken);
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
        return await FluidStaticLiquidRenderer.PageTemplateExistsAsync(themeRoot, candidate, cancellationToken).ConfigureAwait(false)
            ? candidate
            : "standalone-page";
    }

    /// <summary>限制 Page template 名称只能映射到当前 Theme 内的安全模板文件名。</summary>
    private static bool IsSafeTemplateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    }

}
