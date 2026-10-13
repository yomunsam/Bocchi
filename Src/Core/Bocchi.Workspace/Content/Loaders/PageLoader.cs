using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

using YamlDotNet.Core;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>页面加载器。读取 <c>pages/&lt;slug&gt;/index.md</c>。</summary>
public sealed class PageLoader
{
    private readonly MarkdownPipeline _markdown;

    public PageLoader(MarkdownPipeline markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _markdown = markdown;
    }

    public LoadResult<PageDocument> Load(
        ContentLocation location,
        string folderSlug,
        string rawContent,
        string? fileLanguage = null,
        string? defaultLanguage = null,
        string? defaultGroupId = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderSlug);
        ArgumentNullException.ThrowIfNull(rawContent);

        var errors = new List<ContentValidationError>();
        var split = FrontmatterParser.Split(rawContent);

        if (string.IsNullOrWhiteSpace(split.Yaml))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Page, null,
                ContentErrorSeverity.Error, "PAGE_NO_FRONTMATTER",
                "页面缺少 YAML frontmatter。"));
            return LoadResult.Fail<PageDocument>(errors);
        }

        YamlDotNet.RepresentationModel.YamlMappingNode? mapping;
        try
        {
            mapping = YamlAccess.Parse(split.Yaml);
        }
        catch (YamlException ex)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Page, null,
                ContentErrorSeverity.Error, "PAGE_INVALID_YAML",
                $"frontmatter YAML 解析失败：{ex.Message}"));
            return LoadResult.Fail<PageDocument>(errors);
        }

        if (mapping is null)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Page, null,
                ContentErrorSeverity.Error, "PAGE_EMPTY_FRONTMATTER",
                "frontmatter 为空。"));
            return LoadResult.Fail<PageDocument>(errors);
        }

        var title = YamlAccess.GetString(mapping, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Page, "title",
                ContentErrorSeverity.Error, "PAGE_MISSING_TITLE",
                "页面必须提供 title。"));
        }

        var slug = YamlAccess.GetString(mapping, "slug") ?? folderSlug;
        defaultGroupId ??= $"pages/{folderSlug}";
        var localization = ContentLocalizationFrontmatter.Read(
            mapping, fileLanguage, defaultLanguage, defaultGroupId, location, ContentKind.Page, errors);

        var status = PostLoader.ParseStatus(YamlAccess.GetString(mapping, "status"), errors, location, ContentKind.Page);
        var order = YamlAccess.GetInt(mapping, "order") ?? 0;
        var showInNavigation = YamlAccess.GetBool(mapping, "showInNavigation") ?? false;
        var summary = YamlAccess.GetString(mapping, "summary");
        var template = NormalizeTemplateName(YamlAccess.GetString(mapping, "template"));
        var tags = YamlAccess.GetStringList(mapping, "tags");
        if (mapping.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode("tags"), out var tagsNode)
            && tagsNode is YamlDotNet.RepresentationModel.YamlScalarNode { Value: { Length: > 0 } })
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Page, "tags",
                ContentErrorSeverity.Warning, "PAGE_TAGS_NOT_A_LIST",
                "tags 需要写成列表，例如 tags: [a, b]，当前写法会被忽略。"));
        }

        var coverPath = YamlAccess.GetString(mapping, "cover");
        MediaReference? cover = string.IsNullOrWhiteSpace(coverPath)
            ? null
            : new MediaReference(coverPath!);
        var links = ParseLinks(mapping, errors, location);

        if (errors.Any(e => e.Severity == ContentErrorSeverity.Error))
        {
            return LoadResult.Fail<PageDocument>(errors);
        }

        var html = _markdown.RenderHtml(split.Body);
        var excerpt = _markdown.ExtractExcerpt(split.Body);
        var media = _markdown.ExtractMediaReferences(split.Body);

        var page = new Page
        {
            Slug = slug,
            Title = title!,
            Language = localization.Language,
            Localization = localization.Localization,
            Status = status,
            Order = order,
            ShowInNavigation = showInNavigation,
            Summary = summary,
            Template = template,
            Cover = cover,
            Tags = tags,
            Links = links,
        };

        var body = new ContentBody(split.Body, html, excerpt, media);
        return LoadResult.Ok<PageDocument>(new PageDocument(location, page, body), errors);
    }

    /// <summary>解析 links 列表；与 Work 同形（label + url）。</summary>
    private static List<WorkLink> ParseLinks(
        YamlDotNet.RepresentationModel.YamlMappingNode mapping,
        List<ContentValidationError> errors,
        ContentLocation location)
    {
        var seq = YamlAccess.GetSequence(mapping, "links");
        if (seq is null)
        {
            return [];
        }

        var list = new List<WorkLink>();
        foreach (var item in seq.Children)
        {
            if (item is not YamlDotNet.RepresentationModel.YamlMappingNode m)
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, ContentKind.Page, "links",
                    ContentErrorSeverity.Error, "PAGE_INVALID_LINK",
                    "links 项必须是对象（label + url）。"));
                continue;
            }

            var label = YamlAccess.GetString(m, "label");
            var url = YamlAccess.GetString(m, "url");
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, ContentKind.Page, "links",
                    ContentErrorSeverity.Error, "PAGE_INVALID_LINK",
                    "links 项必须同时提供 label 与 url。"));
                continue;
            }

            if (!ContentLinkUrl.IsAllowed(url))
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, ContentKind.Page, "links",
                    ContentErrorSeverity.Error, "PAGE_INVALID_LINK",
                    "links.url 只允许 http、https、mailto 或站内相对路径，不能使用 javascript / data 等危险协议。"));
                continue;
            }

            list.Add(new WorkLink(label, url));
        }

        return list;
    }

    /// <summary>页面模板为空时使用 Theme Contract 固定存在的 normal 模板。</summary>
    private static string NormalizeTemplateName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "normal" : value.Trim();
}
