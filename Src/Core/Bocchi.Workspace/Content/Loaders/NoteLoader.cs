using System.Globalization;
using System.Text.RegularExpressions;

using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

using YamlDotNet.Core;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>
/// 短文加载器。短文为单文件 Markdown：<c>notes/&lt;year&gt;/&lt;filename&gt;.md</c>。
/// frontmatter 可选；正文即短文文本。文件名形如 <c>YYYY-MM-DD-HHMM-&lt;slug&gt;.md</c>，
/// 缺省 frontmatter 时从文件名解析时间与 id。
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
        string fileName,
        string rawContent,
        TimeSpan fallbackOffset)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(year);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(rawContent);

        var errors = new List<ContentValidationError>();
        var split = FrontmatterParser.Split(rawContent);

        YamlDotNet.RepresentationModel.YamlMappingNode? mapping = null;
        if (!string.IsNullOrWhiteSpace(split.Yaml))
        {
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
        }

        var fileBase = Path.GetFileNameWithoutExtension(fileName);
        var (filenamePublishedAt, filenameSlug) = ParseFileName(fileBase, year, fallbackOffset);

        var id = (mapping is not null ? YamlAccess.GetString(mapping, "id") : null) ?? filenameSlug ?? fileBase;
        var status = mapping is not null
            ? PostLoader.ParseStatus(YamlAccess.GetString(mapping, "status"), errors, location, ContentKind.Note)
            : ContentStatus.Published;

        DateTimeOffset? publishedAt = filenamePublishedAt;
        if (mapping is not null)
        {
            var pa = PostLoader.ParseDateTime(mapping, "publishedAt", fallbackOffset, errors, location, ContentKind.Note);
            if (pa is not null)
            {
                publishedAt = pa;
            }
        }

        var tags = mapping is not null ? YamlAccess.GetStringList(mapping, "tags") : [];
        var mediaFromFrontmatter = ParseMediaList(mapping);

        var bodyText = split.Body.Trim();
        if (bodyText.Length == 0)
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, ContentKind.Note, null,
                ContentErrorSeverity.Error, "NOTE_EMPTY_BODY",
                "短文正文为空。"));
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
            Id = id,
            Status = status,
            PublishedAt = publishedAt,
            Text = bodyText,
            Media = allMedia,
            Tags = tags,
        };

        var body = new ContentBody(bodyText, html, excerpt, inlineMedia);
        return LoadResult.Ok<NoteDocument>(new NoteDocument(location, year, note, body), errors);
    }

    private static List<MediaReference> ParseMediaList(YamlDotNet.RepresentationModel.YamlMappingNode? mapping)
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
                case YamlDotNet.RepresentationModel.YamlScalarNode s when !string.IsNullOrWhiteSpace(s.Value):
                    list.Add(new MediaReference(s.Value!));
                    break;
                case YamlDotNet.RepresentationModel.YamlMappingNode m:
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

    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2})(?:-(?<time>\d{4}))?(?:-(?<slug>.+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();

    internal static (DateTimeOffset? PublishedAt, string? Slug) ParseFileName(
        string fileBase, string year, TimeSpan fallbackOffset)
    {
        var m = FileNameRegex().Match(fileBase);
        if (!m.Success)
        {
            return (null, null);
        }

        var date = m.Groups["date"].Value;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return (null, null);
        }

        if (!string.Equals(d.Year.ToString("D4", CultureInfo.InvariantCulture), year, StringComparison.Ordinal))
        {
            // year folder mismatch — treat as no auto datetime
            return (null, m.Groups["slug"].Success ? m.Groups["slug"].Value : null);
        }

        var time = TimeOnly.MinValue;
        if (m.Groups["time"].Success
            && TimeOnly.TryParseExact(m.Groups["time"].Value, "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            time = t;
        }

        var dt = new DateTimeOffset(d.Year, d.Month, d.Day, time.Hour, time.Minute, 0, fallbackOffset);
        var slug = m.Groups["slug"].Success ? m.Groups["slug"].Value : null;
        return (dt, slug);
    }
}