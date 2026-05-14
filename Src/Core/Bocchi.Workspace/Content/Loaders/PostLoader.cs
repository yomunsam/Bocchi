using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

using YamlDotNet.Core;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>
/// 文章加载器。读取 <c>posts/&lt;year&gt;/&lt;slug&gt;/index.md</c>，输出 <see cref="PostDocument"/>。
/// </summary>
public sealed class PostLoader
{
    private readonly MarkdownPipeline _markdown;

    public PostLoader(MarkdownPipeline markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _markdown = markdown;
    }

    public LoadResult<PostDocument> Load(
        ContentLocation location,
        string year,
        string folderSlug,
        string rawContent,
        TimeSpan fallbackOffset)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(year);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderSlug);
        ArgumentNullException.ThrowIfNull(rawContent);

        var errors = new List<ContentValidationError>();
        var split = FrontmatterParser.Split(rawContent);

        if (string.IsNullOrWhiteSpace(split.Yaml))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Post, null,
                ContentErrorSeverity.Error, "POST_NO_FRONTMATTER",
                "文章缺少 YAML frontmatter（首部 --- ... ---）。"));
            return LoadResult.Fail<PostDocument>(errors);
        }

        YamlDotNet.RepresentationModel.YamlMappingNode? mapping;
        try
        {
            mapping = YamlAccess.Parse(split.Yaml);
        }
        catch (YamlException ex)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Post, null,
                ContentErrorSeverity.Error, "POST_INVALID_YAML",
                $"frontmatter YAML 解析失败：{ex.Message}"));
            return LoadResult.Fail<PostDocument>(errors);
        }

        if (mapping is null)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Post, null,
                ContentErrorSeverity.Error, "POST_EMPTY_FRONTMATTER",
                "frontmatter 为空。"));
            return LoadResult.Fail<PostDocument>(errors);
        }

        var title = YamlAccess.GetString(mapping, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Post, "title",
                ContentErrorSeverity.Error, "POST_MISSING_TITLE",
                "文章必须提供 title。"));
        }

        var slug = YamlAccess.GetString(mapping, "slug") ?? folderSlug;

        var status = ParseStatus(YamlAccess.GetString(mapping, "status"), errors, location, ContentKind.Post);
        var publishedAt = ParseDateTime(mapping, "publishedAt", fallbackOffset, errors, location, ContentKind.Post);
        var updatedAt = ParseDateTime(mapping, "updatedAt", fallbackOffset, errors, location, ContentKind.Post);

        var category = YamlAccess.GetString(mapping, "category");
        var tags = YamlAccess.GetStringList(mapping, "tags");
        var summary = YamlAccess.GetString(mapping, "summary");
        var coverPath = YamlAccess.GetString(mapping, "cover");
        MediaReference? cover = string.IsNullOrWhiteSpace(coverPath)
            ? null
            : new MediaReference(coverPath!);

        if (errors.Any(e => e.Severity == ContentErrorSeverity.Error))
        {
            return LoadResult.Fail<PostDocument>(errors);
        }

        var html = _markdown.RenderHtml(split.Body);
        var excerpt = _markdown.ExtractExcerpt(split.Body);
        var media = _markdown.ExtractMediaReferences(split.Body);

        var post = new Post
        {
            Slug = slug,
            Title = title!,
            Status = status,
            PublishedAt = publishedAt,
            UpdatedAt = updatedAt,
            Category = category,
            Tags = tags,
            Summary = summary,
            Cover = cover,
        };

        var body = new ContentBody(split.Body, html, excerpt, media);
        return LoadResult.Ok<PostDocument>(new PostDocument(location, year, post, body), errors);
    }

    internal static ContentStatus ParseStatus(
        string? raw,
        List<ContentValidationError> errors,
        ContentLocation location,
        ContentKind kind)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ContentStatus.Draft;
        }

        if (Enum.TryParse<ContentStatus>(raw, ignoreCase: true, out var s))
        {
            return s;
        }

        errors.Add(new ContentValidationError(
            location.RelativePath, kind, "status",
            ContentErrorSeverity.Error, "INVALID_STATUS",
            $"无法识别的 status: '{raw}'。允许值：Draft / Published / Archived。"));
        return ContentStatus.Draft;
    }

    internal static DateTimeOffset? ParseDateTime(
        YamlDotNet.RepresentationModel.YamlMappingNode mapping,
        string field,
        TimeSpan fallbackOffset,
        List<ContentValidationError> errors,
        ContentLocation location,
        ContentKind kind)
    {
        var raw = YamlAccess.GetString(mapping, field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeFieldParser.TryParse(raw, fallbackOffset, out var v))
        {
            return v;
        }

        errors.Add(new ContentValidationError(
            location.RelativePath, kind, field,
            ContentErrorSeverity.Error, "INVALID_DATETIME",
            $"无法解析时间字段 '{field}'='{raw}'。"));
        return null;
    }
}