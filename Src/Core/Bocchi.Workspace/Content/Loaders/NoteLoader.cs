using System.Text.RegularExpressions;

using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>
/// 短文加载器。短文为目录型内容：<c>notes/&lt;year&gt;/&lt;MMdd&gt;/&lt;HHmm&gt;-&lt;id&gt;/index.md</c>。
/// <c>id</c> 是公开稳定标识，必须由 frontmatter 明确声明，不再从文件路径推导业务 id 或发布时间。
/// </summary>
public sealed partial class NoteLoader
{
    private readonly MarkdownPipeline _markdown;

    public NoteLoader(MarkdownPipeline markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        _markdown = markdown;
    }

    public LoadResult<NoteDocument> Load(
        ContentLocation location,
        string year,
        string monthDay,
        string directoryName,
        string rawContent,
        TimeSpan fallbackOffset)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(year);
        ArgumentException.ThrowIfNullOrWhiteSpace(monthDay);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);
        ArgumentNullException.ThrowIfNull(rawContent);

        var errors = new List<ContentValidationError>();
        var split = FrontmatterParser.Split(rawContent);

        if (string.IsNullOrWhiteSpace(split.Yaml))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, null,
                ContentErrorSeverity.Error, "NOTE_NO_FRONTMATTER",
                "短文必须提供 YAML frontmatter，并声明 id。"));
            return LoadResult.Fail<NoteDocument>(errors);
        }

        YamlMappingNode? mapping;
        try
        {
            mapping = YamlAccess.Parse(split.Yaml);
        }
        catch (YamlException ex)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, null,
                ContentErrorSeverity.Error, "NOTE_INVALID_YAML",
                $"frontmatter YAML 解析失败：{ex.Message}"));
            return LoadResult.Fail<NoteDocument>(errors);
        }

        if (mapping is null)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, null,
                ContentErrorSeverity.Error, "NOTE_EMPTY_FRONTMATTER",
                "短文 frontmatter 不能为空。"));
            return LoadResult.Fail<NoteDocument>(errors);
        }

        var id = YamlAccess.GetString(mapping, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, "id",
                ContentErrorSeverity.Error, "NOTE_MISSING_ID",
                "短文必须声明 8 位小写字母数字 id。"));
        }
        else if (!NoteIdRegex().IsMatch(id))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, "id",
                ContentErrorSeverity.Error, "NOTE_INVALID_ID",
                "短文 id 必须是 8 位小写字母数字。"));
        }

        var directory = NoteDirectoryRegex().Match(directoryName);
        if (!directory.Success)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, null,
                ContentErrorSeverity.Error, "NOTE_INVALID_DIRECTORY",
                "短文目录名必须是 HHmm-<8位小写字母数字id>。"));
        }
        else if (!string.IsNullOrWhiteSpace(id) &&
            !string.Equals(directory.Groups["id"].Value, id, StringComparison.Ordinal))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, "id",
                ContentErrorSeverity.Error, "NOTE_ID_DIRECTORY_MISMATCH",
                "短文 frontmatter id 必须与目录名中的 id 一致。"));
        }

        var status = PostLoader.ParseStatus(YamlAccess.GetString(mapping, "status"), errors, location, ContentKind.Note);
        var publishedAt = PostLoader.ParseDateTime(mapping, "publishedAt", fallbackOffset, errors, location, ContentKind.Note);
        var tags = YamlAccess.GetStringList(mapping, "tags");
        var mediaFromFrontmatter = ParseMediaList(mapping);

        var bodyText = split.Body.Trim();
        if (bodyText.Length == 0)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, null,
                ContentErrorSeverity.Error, "NOTE_EMPTY_BODY",
                "短文正文为空。"));
        }

        if (errors.Any(e => e.Severity == ContentErrorSeverity.Error))
        {
            return LoadResult.Fail<NoteDocument>(errors);
        }

        var html = _markdown.RenderHtml(bodyText);
        var excerpt = _markdown.ExtractExcerpt(bodyText);
        var inlineMedia = _markdown.ExtractMediaReferences(bodyText);
        var allMedia = mediaFromFrontmatter.Count == 0
            ? inlineMedia
            : mediaFromFrontmatter.Concat(inlineMedia).ToList();

        var note = new Note
        {
            Id = id!,
            Status = status,
            PublishedAt = publishedAt,
            Text = bodyText,
            Media = allMedia,
            Tags = tags,
        };

        var body = new ContentBody(bodyText, html, excerpt, inlineMedia);
        return LoadResult.Ok<NoteDocument>(new NoteDocument(location, year, note, body), errors);
    }

    private static List<MediaReference> ParseMediaList(YamlMappingNode? mapping)
    {
        if (mapping is null)
        {
            return [];
        }

        var seq = YamlAccess.GetSequence(mapping, "media");
        if (seq is null)
        {
            return [];
        }

        var list = new List<MediaReference>();
        foreach (var item in seq.Children)
        {
            switch (item)
            {
                case YamlScalarNode s when !string.IsNullOrWhiteSpace(s.Value):
                    list.Add(new MediaReference(s.Value!));
                    break;
                case YamlMappingNode m:
                    var path = YamlAccess.GetString(m, "path");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        list.Add(new MediaReference(path!, YamlAccess.GetString(m, "alt")));
                    }

                    break;
            }
        }

        return list;
    }

    [GeneratedRegex("^[a-z0-9]{8}$", RegexOptions.CultureInvariant)]
    internal static partial Regex NoteIdRegex();

    [GeneratedRegex("^(?<time>\\d{4})-(?<id>[a-z0-9]{8})$", RegexOptions.CultureInvariant)]
    internal static partial Regex NoteDirectoryRegex();
}
