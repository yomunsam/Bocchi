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

    public LoadResult<PageDocument> Load(ContentLocation location, string folderSlug, string rawContent)
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
        var status = PostLoader.ParseStatus(YamlAccess.GetString(mapping, "status"), errors, location, ContentKind.Page);
        var order = YamlAccess.GetInt(mapping, "order") ?? 0;
        var showInNavigation = YamlAccess.GetBool(mapping, "showInNavigation") ?? false;
        var summary = YamlAccess.GetString(mapping, "summary");

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
            Status = status,
            Order = order,
            ShowInNavigation = showInNavigation,
            Summary = summary,
        };

        var body = new ContentBody(split.Body, html, excerpt, media);
        return LoadResult.Ok<PageDocument>(new PageDocument(location, page, body), errors);
    }
}
