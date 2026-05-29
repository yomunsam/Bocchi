using System.Globalization;

using Bocchi.ContentModel;
using Bocchi.Generator.Exceptions;
using Bocchi.Workspace;
using Bocchi.Workspace.Content;
using Bocchi.Workspace.Scanning;

namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 把 <see cref="ScanResult"/> 加工为站点视角的 <see cref="ContentGraph"/>。详见 <c>Docs/Milestones/M3/M3.md §3.3</c>。
/// </summary>
public sealed class ContentGraphBuilder
{
    private readonly BocchiDataLayout _layout;
    private readonly MarkdownPipeline _markdown;

    public ContentGraphBuilder(BocchiDataLayout layout, MarkdownPipeline markdown)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(markdown);
        _layout = layout;
        _markdown = markdown;
    }

    /// <summary>把 <see cref="ScanResult"/> 构建为内容图。</summary>
    /// <param name="scan">扫描结果。</param>
    /// <param name="options">图构建选项。</param>
    /// <returns>内容图。</returns>
    /// <exception cref="ContentGraphException">违反不变量（slug 冲突、媒体缺失、缺 site.yaml 等）。</exception>
    public ContentGraph Build(ScanResult scan, ContentGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(options);

        if (scan.SiteSettings is null)
        {
            throw new ContentGraphException("缺少 site.yaml，无法构建内容图。");
        }

        if (scan.HasErrors && options.FailOnContentError)
        {
            var first = scan.Errors[0];
            throw new ContentGraphException(
                $"扫描产生 {scan.Errors.Count} 条错误，至少一条：[{first.Code}] {first.RelativePath}：{first.Message}");
        }

        var site = BuildSite(scan.SiteSettings);
        var rewriter = new MediaPathRewriter();

        var usedPostCategorySlugs = new HashSet<string>(StringComparer.Ordinal);
        var configuredPostCategories = NormalizePostCategoryNodes(options.PostCategories, usedPostCategorySlugs);
        var configuredPostCategoryLookup = BuildPostCategoryLookup(configuredPostCategories);
        var derivedPostCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var posts = BuildPosts(
            scan.Posts,
            options,
            rewriter,
            configuredPostCategoryLookup,
            derivedPostCategories,
            usedPostCategorySlugs,
            site.Settings.Language);
        EnsureUniqueContentVariants(posts.Select(p => (p.Localization.GroupId, p.Language)), "post");
        EnsureUniqueSiteUrls(posts.Select(p => p.SiteRelativeUrl), "post");
        var postCategories = BuildPostCategories(configuredPostCategories, derivedPostCategories, posts);

        var pages = BuildPages(scan.Pages, options, rewriter, site.Settings.Language);
        EnsureUniqueContentVariants(pages.Select(p => (p.Localization.GroupId, p.Language)), "page");
        EnsureUniqueSiteUrls(pages.Select(p => p.SiteRelativeUrl), "page");

        var works = BuildWorks(scan.Works, options, rewriter, site.Settings.Language);
        EnsureUniqueContentVariants(works.Select(w => (w.Localization.GroupId, w.Language)), "work");
        EnsureUniqueSiteUrls(works.Select(w => w.SiteRelativeUrl), "work");

        var notes = BuildNotes(scan.Notes, options, rewriter);
        EnsureUniqueSlugs(notes.Select(n => (Year: string.Empty, Slug: n.Id)), "note");

        var friends = BuildFriends(scan.FriendLinks, options, rewriter);

        var indices = BuildIndices(posts, works, site.Settings.FeedItemCount);

        return new ContentGraph
        {
            Site = site,
            Posts = posts,
            PostCategories = postCategories,
            Pages = pages,
            Works = works,
            Notes = notes,
            Friends = friends,
            MediaAssets = rewriter.Assets,
            Indices = indices,
        };
    }

    private static GraphSite BuildSite(SiteSettings settings)
    {
        var normalized = settings.BaseUrl.AbsoluteUri.EndsWith('/')
            ? settings.BaseUrl
            : new Uri(settings.BaseUrl.AbsoluteUri + "/");
        return new GraphSite
        {
            Settings = settings,
            NormalizedBaseUrl = normalized,
        };
    }

    private List<GraphPost> BuildPosts(
        IReadOnlyList<PostDocument> docs,
        ContentGraphOptions opts,
        MediaPathRewriter rewriter,
        IReadOnlyDictionary<string, string> configuredCategoryLookup,
        Dictionary<string, string> derivedCategories,
        HashSet<string> usedCategorySlugs,
        string primaryLanguage)
    {
        var list = new List<GraphPost>(docs.Count);
        foreach (var doc in docs)
        {
            if (!opts.IncludeDrafts && doc.Frontmatter.Status != ContentStatus.Published)
            {
                continue;
            }

            EnsureBodyWithinLimit(doc.Body.Markdown, doc.Location.RelativePath, opts);

            var ownerDir = Path.GetDirectoryName(doc.Location.AbsolutePath)
                ?? throw new ContentGraphException($"无法解析 owner 目录：'{doc.Location.RelativePath}'");
            var siteMediaPrefix = string.Format(CultureInfo.InvariantCulture, "/media/posts/{0}/{1}", doc.Year, doc.Frontmatter.Slug);
            var descriptor = string.Format(CultureInfo.InvariantCulture, "posts/{0}/{1}", doc.Year, doc.Frontmatter.Slug);

            var rewrittenMarkdown = rewriter.RewriteMarkdown(doc.Body.Markdown, ownerDir, siteMediaPrefix, descriptor);
            var rewrittenHtml = _markdown.RenderHtml(rewrittenMarkdown);
            var rewrittenCover = doc.Frontmatter.Cover is null
                ? null
                : rewriter.RewriteReference(doc.Frontmatter.Cover, ownerDir, siteMediaPrefix, descriptor);
            var rewrittenMedia = doc.Body.ReferencedMedia
                .Select(m => rewriter.RewriteReference(m, ownerDir, siteMediaPrefix, descriptor))
                .ToList();
            var language = ResolveContentLanguage(doc.Frontmatter.Language, primaryLanguage);
            var groupId = ResolveContentGroup(doc.Frontmatter.Localization, $"posts/{doc.Year}/{doc.Frontmatter.Slug}");
            var contentId = CreateVariantContentId(groupId, language);
            var baseUrl = SiteUrlResolver.PostUrl(doc.Year, doc.Frontmatter.Slug);

            list.Add(new GraphPost
            {
                ContentId = contentId,
                Slug = doc.Frontmatter.Slug,
                Year = doc.Year,
                Title = doc.Frontmatter.Title,
                Language = language,
                Localization = CreateGraphLocalization(groupId, doc.Frontmatter.Localization),
                Status = doc.Frontmatter.Status,
                PublishedAt = doc.Frontmatter.PublishedAt,
                UpdatedAt = doc.Frontmatter.UpdatedAt,
                Category = doc.Frontmatter.Category,
                CategorySlug = ResolvePostCategorySlug(
                    doc.Frontmatter.Category,
                    configuredCategoryLookup,
                    derivedCategories,
                    usedCategorySlugs),
                Tags = doc.Frontmatter.Tags,
                Summary = doc.Frontmatter.Summary,
                Cover = rewrittenCover,
                SiteRelativeUrl = ApplyPrimaryUnprefixedUrlPolicy(baseUrl, language, primaryLanguage),
                BodyMarkdown = rewrittenMarkdown,
                BodyHtml = rewrittenHtml,
                Excerpt = doc.Body.Excerpt,
                Media = rewrittenMedia,
            });
        }

        var ordered = list
            .OrderByDescending(p => p.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Slug, StringComparer.Ordinal)
            .ToList();
        return ordered
            .Select(post => post with
            {
                Localization = post.Localization with
                {
                    Alternates = CreateAlternates(
                        ordered.Where(other => string.Equals(other.Localization.GroupId, post.Localization.GroupId, StringComparison.Ordinal))
                            .Select(other => (other.ContentId, other.Language, other.Title, other.SiteRelativeUrl))),
                },
            })
            .ToList();
    }

    /// <summary>把外部 Category 快照清理为 Generator 内部树，并确保整棵树 slug 唯一。</summary>
    private static List<NormalizedPostCategory> NormalizePostCategoryNodes(
        IEnumerable<BuildCategoryNode> nodes,
        HashSet<string> usedSlugs)
        => NormalizePostCategoryNodes(nodes, usedSlugs, depth: 0);

    private static List<NormalizedPostCategory> NormalizePostCategoryNodes(
        IEnumerable<BuildCategoryNode> nodes,
        HashSet<string> usedSlugs,
        int depth)
    {
        if (depth >= 5)
        {
            return [];
        }

        var result = new List<NormalizedPostCategory>();
        foreach (var node in nodes)
        {
            var name = string.IsNullOrWhiteSpace(node.Name) ? string.Empty : node.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var slug = EnsureUniqueCategorySlug(CreateCategorySlug(node.Slug, name, node.Id), usedSlugs);
            result.Add(new NormalizedPostCategory(
                name,
                slug,
                NormalizePostCategoryNodes(node.Children, usedSlugs, depth + 1)));
        }

        return result;
    }

    /// <summary>建立 name/slug 到稳定 slug 的查找表，让 Post frontmatter 可以用类别名或 slug 匹配 Category tree。</summary>
    private static Dictionary<string, string> BuildPostCategoryLookup(IEnumerable<NormalizedPostCategory> nodes)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in FlattenPostCategories(nodes))
        {
            result.TryAdd(node.Name, node.Slug);
            result.TryAdd(node.Slug, node.Slug);
        }

        return result;
    }

    /// <summary>解析文章 Category slug；未配置的 category 会派生成根级 Category，避免 posts.json 指向不可生成的页面。</summary>
    private static string? ResolvePostCategorySlug(
        string? category,
        IReadOnlyDictionary<string, string> configuredCategoryLookup,
        Dictionary<string, string> derivedCategories,
        HashSet<string> usedCategorySlugs)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var name = category.Trim();
        if (configuredCategoryLookup.TryGetValue(name, out var configuredSlug))
        {
            return configuredSlug;
        }

        if (derivedCategories.TryGetValue(name, out var derivedSlug))
        {
            return derivedSlug;
        }

        var slug = EnsureUniqueCategorySlug(CreateCategorySlug(null, name, name), usedCategorySlugs);
        derivedCategories[name] = slug;
        return slug;
    }

    /// <summary>创建最终输出的 Post Category tree；配置树优先，文章中出现但未配置的 category 追加为根节点。</summary>
    private static List<GraphPostCategory> BuildPostCategories(
        IEnumerable<NormalizedPostCategory> configuredCategories,
        IReadOnlyDictionary<string, string> derivedCategories,
        IReadOnlyList<GraphPost> posts)
    {
        var counts = posts
            .Where(post => !string.IsNullOrWhiteSpace(post.CategorySlug))
            .GroupBy(post => post.CategorySlug!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var result = configuredCategories.Select(node => ToGraphPostCategory(node, counts)).ToList();
        foreach (var (name, slug) in derivedCategories.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new GraphPostCategory
            {
                Name = name,
                Slug = slug,
                SiteRelativeUrl = SiteUrlResolver.PostCategoryUrl(slug),
                Count = counts.GetValueOrDefault(slug),
            });
        }

        return result;
    }

    private static GraphPostCategory ToGraphPostCategory(
        NormalizedPostCategory node,
        IReadOnlyDictionary<string, int> counts)
        => new()
        {
            Name = node.Name,
            Slug = node.Slug,
            SiteRelativeUrl = SiteUrlResolver.PostCategoryUrl(node.Slug),
            Count = counts.GetValueOrDefault(node.Slug),
            Children = node.Children.Select(child => ToGraphPostCategory(child, counts)).ToArray(),
        };

    private static IEnumerable<NormalizedPostCategory> FlattenPostCategories(IEnumerable<NormalizedPostCategory> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in FlattenPostCategories(node.Children))
            {
                yield return child;
            }
        }
    }

    private static string CreateCategorySlug(string? explicitSlug, string name, string id)
    {
        var slug = CategorySlug.Normalize(explicitSlug);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            return slug;
        }

        slug = CategorySlug.Normalize(name);
        if (!string.IsNullOrWhiteSpace(slug))
        {
            return slug;
        }

        slug = CategorySlug.Normalize(id);
        return string.IsNullOrWhiteSpace(slug) ? "category" : slug;
    }

    private static string EnsureUniqueCategorySlug(string baseSlug, HashSet<string> usedSlugs)
    {
        var normalized = string.IsNullOrWhiteSpace(baseSlug) ? "category" : baseSlug;
        var candidate = normalized;
        var suffix = 2;
        while (!usedSlugs.Add(candidate))
        {
            candidate = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", normalized, suffix);
            suffix++;
        }

        return candidate;
    }

    private List<GraphPage> BuildPages(
        IReadOnlyList<PageDocument> docs, ContentGraphOptions opts, MediaPathRewriter rewriter, string primaryLanguage)
    {
        var list = new List<GraphPage>(docs.Count);
        foreach (var doc in docs)
        {
            if (!opts.IncludeDrafts && doc.Frontmatter.Status != ContentStatus.Published)
            {
                continue;
            }

            EnsureBodyWithinLimit(doc.Body.Markdown, doc.Location.RelativePath, opts);

            var ownerDir = Path.GetDirectoryName(doc.Location.AbsolutePath)
                ?? throw new ContentGraphException($"无法解析 owner 目录：'{doc.Location.RelativePath}'");
            var siteMediaPrefix = string.Format(CultureInfo.InvariantCulture, "/media/pages/{0}", doc.Frontmatter.Slug);
            var descriptor = string.Format(CultureInfo.InvariantCulture, "pages/{0}", doc.Frontmatter.Slug);

            var rewrittenMarkdown = rewriter.RewriteMarkdown(doc.Body.Markdown, ownerDir, siteMediaPrefix, descriptor);
            var rewrittenHtml = _markdown.RenderHtml(rewrittenMarkdown);
            var rewrittenMedia = doc.Body.ReferencedMedia
                .Select(m => rewriter.RewriteReference(m, ownerDir, siteMediaPrefix, descriptor))
                .ToList();
            var language = ResolveContentLanguage(doc.Frontmatter.Language, primaryLanguage);
            var groupId = ResolveContentGroup(doc.Frontmatter.Localization, $"pages/{doc.Frontmatter.Slug}");
            var contentId = CreateVariantContentId(groupId, language);
            var baseUrl = SiteUrlResolver.PageUrl(doc.Frontmatter.Slug);

            list.Add(new GraphPage
            {
                ContentId = contentId,
                Slug = doc.Frontmatter.Slug,
                Title = doc.Frontmatter.Title,
                Language = language,
                Localization = CreateGraphLocalization(groupId, doc.Frontmatter.Localization),
                Status = doc.Frontmatter.Status,
                Order = doc.Frontmatter.Order,
                ShowInNavigation = doc.Frontmatter.ShowInNavigation,
                Summary = doc.Frontmatter.Summary,
                Template = doc.Frontmatter.Template,
                SiteRelativeUrl = ApplyPrimaryUnprefixedUrlPolicy(baseUrl, language, primaryLanguage),
                BodyMarkdown = rewrittenMarkdown,
                BodyHtml = rewrittenHtml,
                Excerpt = doc.Body.Excerpt,
                Media = rewrittenMedia,
            });
        }

        var ordered = list
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Title, StringComparer.Ordinal)
            .ToList();
        return ordered
            .Select(page => page with
            {
                Localization = page.Localization with
                {
                    Alternates = CreateAlternates(
                        ordered.Where(other => string.Equals(other.Localization.GroupId, page.Localization.GroupId, StringComparison.Ordinal))
                            .Select(other => (other.ContentId, other.Language, other.Title, other.SiteRelativeUrl))),
                },
            })
            .ToList();
    }

    private List<GraphWork> BuildWorks(
        IReadOnlyList<WorkDocument> docs, ContentGraphOptions opts, MediaPathRewriter rewriter, string primaryLanguage)
    {
        var list = new List<GraphWork>(docs.Count);
        foreach (var doc in docs)
        {
            if (!opts.IncludeDrafts && doc.Frontmatter.Status != ContentStatus.Published)
            {
                continue;
            }

            EnsureBodyWithinLimit(doc.Body.Markdown, doc.Location.RelativePath, opts);

            var ownerDir = Path.GetDirectoryName(doc.Location.AbsolutePath)
                ?? throw new ContentGraphException($"无法解析 owner 目录：'{doc.Location.RelativePath}'");
            var siteMediaPrefix = string.Format(CultureInfo.InvariantCulture, "/media/works/{0}/{1}", doc.Year, doc.Frontmatter.Slug);
            var descriptor = string.Format(CultureInfo.InvariantCulture, "works/{0}/{1}", doc.Year, doc.Frontmatter.Slug);

            var rewrittenMarkdown = rewriter.RewriteMarkdown(doc.Body.Markdown, ownerDir, siteMediaPrefix, descriptor);
            var rewrittenHtml = _markdown.RenderHtml(rewrittenMarkdown);
            var rewrittenCover = doc.Frontmatter.Cover is null
                ? null
                : rewriter.RewriteReference(doc.Frontmatter.Cover, ownerDir, siteMediaPrefix, descriptor);
            var rewrittenMedia = doc.Body.ReferencedMedia
                .Select(m => rewriter.RewriteReference(m, ownerDir, siteMediaPrefix, descriptor))
                .ToList();
            var language = ResolveContentLanguage(doc.Frontmatter.Language, primaryLanguage);
            var groupId = ResolveContentGroup(doc.Frontmatter.Localization, $"works/{doc.Year}/{doc.Frontmatter.Slug}");
            var contentId = CreateVariantContentId(groupId, language);
            var baseUrl = SiteUrlResolver.WorkUrl(doc.Year, doc.Frontmatter.Slug);

            list.Add(new GraphWork
            {
                ContentId = contentId,
                Slug = doc.Frontmatter.Slug,
                Year = doc.Year,
                Title = doc.Frontmatter.Title,
                Language = language,
                Localization = CreateGraphLocalization(groupId, doc.Frontmatter.Localization),
                Status = doc.Frontmatter.Status,
                Role = doc.Frontmatter.Role,
                Period = doc.Frontmatter.Period,
                Cover = rewrittenCover,
                Links = doc.Frontmatter.Links,
                Stack = doc.Frontmatter.Stack,
                Summary = doc.Frontmatter.Summary,
                Featured = doc.Frontmatter.Featured,
                SiteRelativeUrl = ApplyPrimaryUnprefixedUrlPolicy(baseUrl, language, primaryLanguage),
                BodyMarkdown = rewrittenMarkdown,
                BodyHtml = rewrittenHtml,
                Excerpt = doc.Body.Excerpt,
                Media = rewrittenMedia,
            });
        }

        var ordered = list
            .OrderByDescending(w => w.Featured)
            .ThenByDescending(w => w.Year, StringComparer.Ordinal)
            .ThenBy(w => w.Title, StringComparer.Ordinal)
            .ToList();
        return ordered
            .Select(work => work with
            {
                Localization = work.Localization with
                {
                    Alternates = CreateAlternates(
                        ordered.Where(other => string.Equals(other.Localization.GroupId, work.Localization.GroupId, StringComparison.Ordinal))
                            .Select(other => (other.ContentId, other.Language, other.Title, other.SiteRelativeUrl))),
                },
            })
            .ToList();
    }

    private static string ResolveContentLanguage(string? language, string primaryLanguage)
        => string.IsNullOrWhiteSpace(language) ? primaryLanguage : language.Trim();

    private static string ResolveContentGroup(ContentLocalization? localization, string fallbackGroupId)
        => string.IsNullOrWhiteSpace(localization?.GroupId) ? fallbackGroupId : localization.GroupId;

    private static string CreateVariantContentId(string groupId, string language)
        => string.Format(CultureInfo.InvariantCulture, "{0}@{1}", groupId, language);

    private static GraphContentLocalization CreateGraphLocalization(
        string groupId,
        ContentLocalization? localization)
    {
        var sourceLanguage = localization?.TranslationOf?.Language;
        var sourceContentId = localization?.TranslationOf?.ContentId ??
            (string.IsNullOrWhiteSpace(sourceLanguage) ? null : CreateVariantContentId(groupId, sourceLanguage));
        return new GraphContentLocalization
        {
            GroupId = groupId,
            IsTranslation = localization?.TranslationOf is not null,
            SourceLanguage = sourceLanguage,
            SourceContentId = sourceContentId,
        };
    }

    private static GraphContentAlternate[] CreateAlternates(
        IEnumerable<(string ContentId, string Language, string Title, string Url)> variants)
        => variants
            .OrderBy(variant => variant.Language, StringComparer.OrdinalIgnoreCase)
            .Select(variant => new GraphContentAlternate
            {
                ContentId = variant.ContentId,
                Language = variant.Language,
                Title = variant.Title,
                Url = variant.Url,
            })
            .ToArray();

    private static string ApplyPrimaryUnprefixedUrlPolicy(
        string siteRelativeUrl,
        string language,
        string primaryLanguage)
    {
        if (string.Equals(language, primaryLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return siteRelativeUrl;
        }

        var normalized = siteRelativeUrl.StartsWith('/') ? siteRelativeUrl : "/" + siteRelativeUrl;
        return normalized == "/"
            ? string.Format(CultureInfo.InvariantCulture, "/{0}/", language)
            : string.Format(CultureInfo.InvariantCulture, "/{0}{1}", language, normalized);
    }

    private List<GraphNote> BuildNotes(
        IReadOnlyList<NoteDocument> docs, ContentGraphOptions opts, MediaPathRewriter rewriter)
    {
        var list = new List<GraphNote>(docs.Count);
        foreach (var doc in docs)
        {
            if (!opts.IncludeDrafts && doc.Frontmatter.Status != ContentStatus.Published)
            {
                continue;
            }

            EnsureBodyWithinLimit(doc.Body.Markdown, doc.Location.RelativePath, opts);

            var ownerDir = Path.GetDirectoryName(doc.Location.AbsolutePath)
                ?? throw new ContentGraphException($"无法解析 owner 目录：'{doc.Location.RelativePath}'");
            var siteMediaPrefix = string.Format(CultureInfo.InvariantCulture, "/media/notes/{0}", doc.Frontmatter.Id);
            var descriptor = string.Format(CultureInfo.InvariantCulture, "notes/{0}", doc.Frontmatter.Id);

            var rewrittenMarkdown = rewriter.RewriteMarkdown(doc.Body.Markdown, ownerDir, siteMediaPrefix, descriptor);
            var rewrittenHtml = _markdown.RenderHtml(rewrittenMarkdown);
            var rewrittenMedia = doc.Body.ReferencedMedia
                .Select(m => rewriter.RewriteReference(m, ownerDir, siteMediaPrefix, descriptor))
                .ToList();
            // Note 的 frontmatter.Media 字段：M3 也一并改写
            var rewrittenFmMedia = doc.Frontmatter.Media
                .Select(m => rewriter.RewriteReference(m, ownerDir, siteMediaPrefix, descriptor))
                .ToList();
            var union = rewrittenMedia
                .Concat(rewrittenFmMedia)
                .GroupBy(m => m.Path, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();

            list.Add(new GraphNote
            {
                Id = doc.Frontmatter.Id,
                Year = doc.Year,
                SiteRelativeUrl = SiteUrlResolver.NoteUrl(doc.Frontmatter.Id),
                Status = doc.Frontmatter.Status,
                PublishedAt = doc.Frontmatter.PublishedAt,
                Tags = doc.Frontmatter.Tags,
                BodyMarkdown = rewrittenMarkdown,
                BodyHtml = rewrittenHtml,
                Excerpt = doc.Body.Excerpt,
                Media = union,
            });
        }

        return list
            .OrderByDescending(n => n.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<GraphFriend> BuildFriends(
        IReadOnlyList<FriendLink> friends, ContentGraphOptions opts, MediaPathRewriter rewriter)
    {
        var list = new List<GraphFriend>(friends.Count);
        // friends.yaml 中的头像 path 形如 "assets/<file>" 相对 friends/ 目录。
        var friendsDir = opts.FriendsDirectoryAbsolute;
        const string siteMediaPrefix = "/media/friends";
        const string descriptor = "friends";

        foreach (var friend in friends)
        {
            if (!opts.IncludeDrafts && friend.Status != ContentStatus.Published)
            {
                continue;
            }

            MediaReference? rewrittenAvatar = null;
            if (friend.Avatar is not null)
            {
                rewrittenAvatar = rewriter.RewriteReference(friend.Avatar, friendsDir, siteMediaPrefix, descriptor);
            }

            list.Add(new GraphFriend
            {
                Name = friend.Name,
                Url = friend.Url,
                Avatar = rewrittenAvatar,
                Description = friend.Description,
                Tags = friend.Tags,
                Status = friend.Status,
                Order = friend.Order,
            });
        }

        return list
            .OrderBy(f => f.Order)
            .ThenBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static GraphIndices BuildIndices(
        IReadOnlyList<GraphPost> posts, IReadOnlyList<GraphWork> works, int latestCount)
    {
        var latest = posts.Take(latestCount > 0 ? latestCount : 20).ToList();

        var byYear = posts
            .GroupBy(p => p.Year, StringComparer.Ordinal)
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, IReadOnlyList<GraphPost>>(g.Key, g.ToList()))
            .ToList();

        var byTag = posts
            .SelectMany(p => p.Tags.Select(t => (Tag: t.Trim(), Post: p)))
            .Where(x => !string.IsNullOrEmpty(x.Tag))
            .GroupBy(x => x.Tag, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, IReadOnlyList<GraphPost>>(
                g.Key, g.Select(x => x.Post).ToList()))
            .ToList();

        var byCategory = posts
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Category) ? "_uncategorized" : p.Category!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, IReadOnlyList<GraphPost>>(g.Key, g.ToList()))
            .ToList();

        var worksByYear = works
            .GroupBy(w => w.Year, StringComparer.Ordinal)
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, IReadOnlyList<GraphWork>>(g.Key, g.ToList()))
            .ToList();

        return new GraphIndices(latest, byYear, byTag, byCategory, worksByYear);
    }

    private static void EnsureUniqueContentVariants(IEnumerable<(string GroupId, string Language)> keys, string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            var stableKey = string.Format(CultureInfo.InvariantCulture, "{0}\u001f{1}", key.GroupId, key.Language);
            if (!seen.Add(stableKey))
            {
                throw new ContentGraphException(
                    $"{kind} localization group '{key.GroupId}' 中 language='{key.Language}' 重复。");
            }
        }
    }

    private static void EnsureUniqueSiteUrls(IEnumerable<string> urls, string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in urls)
        {
            if (!seen.Add(url))
            {
                throw new ContentGraphException($"{kind} URL '{url}' 重复。");
            }
        }
    }

    private static void EnsureUniqueSlugs(IEnumerable<(string Year, string Slug)> keys, string kind)
    {
        var seen = new HashSet<(string, string)>();
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                throw new ContentGraphException(
                    string.IsNullOrEmpty(key.Year)
                        ? $"{kind} slug '{key.Slug}' 重复。"
                        : $"{kind} slug '{key.Year}/{key.Slug}' 重复。");
            }
        }
    }

    private static void EnsureBodyWithinLimit(string body, string descriptor, ContentGraphOptions opts)
    {
        if (opts.MaxBodyBytes > 0 && System.Text.Encoding.UTF8.GetByteCount(body) > opts.MaxBodyBytes)
        {
            throw new ContentGraphException(
                $"内容 '{descriptor}' 的 Markdown 正文超过限制（{opts.MaxBodyBytes} 字节）。");
        }
    }

    /// <summary>Generator 内部使用的标准化 Category 节点。</summary>
    private sealed record NormalizedPostCategory(
        string Name,
        string Slug,
        IReadOnlyList<NormalizedPostCategory> Children);
}

/// <summary>
/// 内容图构建选项。
/// </summary>
public sealed record ContentGraphOptions
{
    /// <summary>是否纳入草稿（<c>status: draft</c> 的内容）。默认 <c>false</c>。</summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>扫描期出现 Error 时是否阻止构图。默认 <c>true</c>。</summary>
    public bool FailOnContentError { get; init; } = true;

    /// <summary>单篇 markdown 正文字节上限（UTF-8）。默认 5 MiB。<c>&lt;= 0</c> 表示不限制。</summary>
    public int MaxBodyBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>friends.yaml 所在目录的绝对路径（用于解析头像相对路径）。</summary>
    public required string FriendsDirectoryAbsolute { get; init; }

    /// <summary>外部注入的 Post Category tree；为空时按文章 frontmatter category 派生扁平 tree。</summary>
    public IReadOnlyList<BuildCategoryNode> PostCategories { get; init; } = [];
}
