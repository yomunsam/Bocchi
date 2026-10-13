using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Utilities;

namespace Bocchi.Generator.SiteArtifacts;

/// <summary>生成静态搜索索引 <c>search-index.json</c>，供主题在浏览器端检索。</summary>
public static partial class SearchIndexGenerator
{
    private const string StageName = "WriteSiteArtifactsStage";
    private const int MaxTextCharsPerItem = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>从内容图构建搜索索引 artifact。</summary>
    public static (BuildArtifact Artifact, ReadOnlyMemory<byte> Bytes) Build(ContentGraph.ContentGraph graph, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var items = new List<SearchIndexItem>();
        foreach (var post in graph.Posts)
        {
            TryAdd(items, CreateItem(
                id: post.ContentId,
                kind: "post",
                language: post.Language,
                title: post.Title,
                url: post.SiteRelativeUrl,
                summary: post.Summary ?? post.Excerpt,
                tags: post.Tags,
                html: post.BodyHtml));
        }

        foreach (var page in graph.Pages)
        {
            TryAdd(items, CreateItem(
                id: page.ContentId,
                kind: "page",
                language: page.Language,
                title: page.Title,
                url: page.SiteRelativeUrl,
                summary: page.Summary ?? page.Excerpt,
                tags: page.Tags,
                html: page.BodyHtml));
        }

        foreach (var work in graph.Works)
        {
            TryAdd(items, CreateItem(
                id: work.ContentId,
                kind: "work",
                language: work.Language,
                title: work.Title,
                url: work.SiteRelativeUrl,
                summary: work.Summary ?? work.Excerpt,
                tags: work.Stack,
                html: work.BodyHtml));
        }

        foreach (var note in graph.Notes)
        {
            TryAdd(items, CreateItem(
                id: note.Id,
                kind: "note",
                language: graph.Site.Settings.Language,
                title: note.Excerpt ?? note.Id,
                url: note.SiteRelativeUrl,
                summary: note.Excerpt,
                tags: note.Tags,
                html: note.BodyHtml));
        }

        var payload = new SearchIndexDocument(
            GeneratedAt: generatedAt,
            ItemCount: items.Count,
            Items: items);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var artifact = new BuildArtifact
        {
            Path = "/search-index.json",
            Kind = ArtifactKind.SiteArtifact,
            ContentType = "application/json; charset=utf-8",
            SizeBytes = bytes.Length,
            Sha256 = Sha256Hex.FromBytes(bytes),
            ProducedBy = StageName,
            Bytes = bytes,
        };
        return (artifact, bytes);
    }


    private static void TryAdd(List<SearchIndexItem> items, SearchIndexItem? item)
    {
        if (item is not null)
        {
            items.Add(item);
        }
    }

    private static SearchIndexItem? CreateItem(
        string id,
        string kind,
        string language,
        string title,
        string url,
        string? summary,
        IReadOnlyList<string> tags,
        string html)
    {
        if (!SiteRelativeHref.IsAllowed(url))
        {
            return null;
        }

        var text = Truncate(ToPlainText(html), MaxTextCharsPerItem);
        return new SearchIndexItem(
            Id: id,
            Kind: kind,
            Language: language,
            Title: title,
            Url: url.Trim(),
            Summary: string.IsNullOrWhiteSpace(summary) ? null : Truncate(summary.Trim(), 280),
            Tags: tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.Ordinal).ToArray(),
            Text: text);
    }

    /// <summary>去掉 HTML 标签，解码实体，压空白，便于子串匹配。</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags) ?? withoutTags;
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string Truncate(string value, int maxChars)
        => value.Length <= maxChars ? value : value[..maxChars];

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed record SearchIndexDocument(
        DateTimeOffset GeneratedAt,
        int ItemCount,
        IReadOnlyList<SearchIndexItem> Items);

    private sealed record SearchIndexItem(
        string Id,
        string Kind,
        string Language,
        string Title,
        string Url,
        string? Summary,
        IReadOnlyList<string> Tags,
        string Text);
}
