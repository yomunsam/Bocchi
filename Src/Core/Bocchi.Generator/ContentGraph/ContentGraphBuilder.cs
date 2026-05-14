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
    private readonly WorkspaceLayout _layout;
    private readonly MarkdownPipeline _markdown;

    public ContentGraphBuilder(WorkspaceLayout layout, MarkdownPipeline markdown)
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

        var posts = BuildPosts(scan.Posts, options, rewriter);
        EnsureUniqueSlugs(posts.Select(p => (Year: p.Year, Slug: p.Slug)), "post");

        var pages = BuildPages(scan.Pages, options, rewriter);
        EnsureUniqueSlugs(pages.Select(p => (Year: string.Empty, Slug: p.Slug)), "page");

        var works = BuildWorks(scan.Works, options, rewriter);
        EnsureUniqueSlugs(works.Select(w => (Year: w.Year, Slug: w.Slug)), "work");

        var notes = BuildNotes(scan.Notes, options, rewriter);
        EnsureUniqueSlugs(notes.Select(n => (Year: n.Year, Slug: n.Id)), "note");

        var friends = BuildFriends(scan.FriendLinks, options, rewriter);

        var indices = BuildIndices(posts, works, site.Settings.FeedItemCount);

        return new ContentGraph
        {
            Site = site,
            Posts = posts,
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
        IReadOnlyList<PostDocument> docs, ContentGraphOptions opts, MediaPathRewriter rewriter)
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

            list.Add(new GraphPost
            {
                Slug = doc.Frontmatter.Slug,
                Year = doc.Year,
                Title = doc.Frontmatter.Title,
                Status = doc.Frontmatter.Status,
                PublishedAt = doc.Frontmatter.PublishedAt,
                UpdatedAt = doc.Frontmatter.UpdatedAt,
                Category = doc.Frontmatter.Category,
                Tags = doc.Frontmatter.Tags,
                Summary = doc.Frontmatter.Summary,
                Cover = rewrittenCover,
                SiteRelativeUrl = SiteUrlResolver.PostUrl(doc.Year, doc.Frontmatter.Slug),
                BodyMarkdown = rewrittenMarkdown,
                BodyHtml = rewrittenHtml,
                Excerpt = doc.Body.Excerpt,
                Media = rewrittenMedia,
            });
        }

        return list
            .OrderByDescending(p => p.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenBy(p => p.Slug, StringComparer.Ordinal)
            .ToList();
    }

    private List<GraphPage> BuildPages(
        IReadOnlyList<PageDocument> docs, ContentGraphOptions opts, MediaPathRewriter rewriter)
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

            list.Add(new GraphPage
            {
                Slug = doc.Frontmatter.Slug,
                Title = doc.Frontmatter.Title,
                Status = doc.Frontmatter.Status,
                Order = doc.Frontmatter.Order,
                ShowInNavigation = doc.Frontmatter.ShowInNavigation,
                Summary = doc.Frontmatter.Summary,
                SiteRelativeUrl = SiteUrlResolver.PageUrl(doc.Frontmatter.Slug),
                BodyMarkdown = rewrittenMarkdown,
                BodyHtml = rewrittenHtml,
                Excerpt = doc.Body.Excerpt,
                Media = rewrittenMedia,
            });
        }

        return list
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Title, StringComparer.Ordinal)
            .ToList();
    }

    private List<GraphWork> BuildWorks(
        IReadOnlyList<WorkDocument> docs, ContentGraphOptions opts, MediaPathRewriter rewriter)
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

            list.Add(new GraphWork
            {
                Slug = doc.Frontmatter.Slug,
                Year = doc.Year,
                Title = doc.Frontmatter.Title,
                Status = doc.Frontmatter.Status,
                Role = doc.Frontmatter.Role,
                Period = doc.Frontmatter.Period,
                Cover = rewrittenCover,
                Links = doc.Frontmatter.Links,
                Stack = doc.Frontmatter.Stack,
                Summary = doc.Frontmatter.Summary,
                Featured = doc.Frontmatter.Featured,
                SiteRelativeUrl = SiteUrlResolver.WorkUrl(doc.Year, doc.Frontmatter.Slug),
                BodyMarkdown = rewrittenMarkdown,
                BodyHtml = rewrittenHtml,
                Excerpt = doc.Body.Excerpt,
                Media = rewrittenMedia,
            });
        }

        return list
            .OrderByDescending(w => w.Featured)
            .ThenByDescending(w => w.Year, StringComparer.Ordinal)
            .ThenBy(w => w.Title, StringComparer.Ordinal)
            .ToList();
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
            var siteMediaPrefix = string.Format(CultureInfo.InvariantCulture, "/media/notes/{0}", doc.Year);
            var descriptor = string.Format(CultureInfo.InvariantCulture, "notes/{0}/{1}", doc.Year, doc.Frontmatter.Id);

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
}