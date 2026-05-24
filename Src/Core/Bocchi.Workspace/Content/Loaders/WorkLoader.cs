using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>作品加载器。读取 <c>works/&lt;year&gt;/&lt;slug&gt;/index.md</c>。</summary>
public sealed class WorkLoader
{
    private readonly MarkdownPipeline _markdown;

    public WorkLoader(MarkdownPipeline markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _markdown = markdown;
    }

    public LoadResult<WorkDocument> Load(
        ContentLocation location,
        string year,
        string folderSlug,
        string rawContent,
        string? fileLanguage = null,
        string? defaultLanguage = null,
        string? defaultGroupId = null)
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
                location.RelativePath, ContentKind.Work, null,
                ContentErrorSeverity.Error, "WORK_NO_FRONTMATTER",
                "作品缺少 YAML frontmatter。"));
            return LoadResult.Fail<WorkDocument>(errors);
        }

        YamlMappingNode? mapping;
        try
        {
            mapping = YamlAccess.Parse(split.Yaml);
        }
        catch (YamlException ex)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Work, null,
                ContentErrorSeverity.Error, "WORK_INVALID_YAML",
                $"frontmatter YAML 解析失败：{ex.Message}"));
            return LoadResult.Fail<WorkDocument>(errors);
        }

        if (mapping is null)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Work, null,
                ContentErrorSeverity.Error, "WORK_EMPTY_FRONTMATTER",
                "frontmatter 为空。"));
            return LoadResult.Fail<WorkDocument>(errors);
        }

        var title = YamlAccess.GetString(mapping, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Work, "title",
                ContentErrorSeverity.Error, "WORK_MISSING_TITLE",
                "作品必须提供 title。"));
        }

        var slug = YamlAccess.GetString(mapping, "slug") ?? folderSlug;
        defaultGroupId ??= $"works/{year}/{folderSlug}";
        var localization = ContentLocalizationFrontmatter.Read(
            mapping, fileLanguage, defaultLanguage, defaultGroupId, location, ContentKind.Work, errors);

        var status = PostLoader.ParseStatus(YamlAccess.GetString(mapping, "status"), errors, location, ContentKind.Work);
        var role = YamlAccess.GetString(mapping, "role");
        var period = YamlAccess.GetString(mapping, "period");
        var coverPath = YamlAccess.GetString(mapping, "cover");
        var cover = string.IsNullOrWhiteSpace(coverPath) ? null : new MediaReference(coverPath!);
        var stack = YamlAccess.GetStringList(mapping, "stack");
        var summary = YamlAccess.GetString(mapping, "summary");
        var featured = YamlAccess.GetBool(mapping, "featured") ?? false;
        var links = ParseLinks(mapping, errors, location);

        if (errors.Any(e => e.Severity == ContentErrorSeverity.Error))
        {
            return LoadResult.Fail<WorkDocument>(errors);
        }

        var html = _markdown.RenderHtml(split.Body);
        var excerpt = _markdown.ExtractExcerpt(split.Body);
        var media = _markdown.ExtractMediaReferences(split.Body);

        var work = new Work
        {
            Slug = slug,
            Title = title!,
            Language = localization.Language,
            Localization = localization.Localization,
            Status = status,
            Role = role,
            Period = period,
            Cover = cover,
            Links = links,
            Stack = stack,
            Summary = summary,
            Featured = featured,
        };

        var body = new ContentBody(split.Body, html, excerpt, media);
        return LoadResult.Ok<WorkDocument>(new WorkDocument(location, year, work, body), errors);
    }

    private static List<WorkLink> ParseLinks(
        YamlMappingNode mapping, List<ContentValidationError> errors, ContentLocation location)
    {
        var seq = YamlAccess.GetSequence(mapping, "links");
        if (seq is null)
        {
            return [];
        }

        var list = new List<WorkLink>();
        foreach (var item in seq.Children)
        {
            if (item is not YamlMappingNode m)
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, ContentKind.Work, "links",
                    ContentErrorSeverity.Error, "WORK_INVALID_LINK",
                    "links 项必须是对象（label + url）。"));
                continue;
            }

            var label = YamlAccess.GetString(m, "label");
            var url = YamlAccess.GetString(m, "url");
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, ContentKind.Work, "links",
                    ContentErrorSeverity.Error, "WORK_INVALID_LINK",
                    "links 项必须同时提供 label 与 url。"));
                continue;
            }

            list.Add(new WorkLink(label, url));
        }

        return list;
    }
}
