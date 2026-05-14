using System.Globalization;
using System.Text;
using System.Xml;
using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Pipeline;

namespace Bocchi.Generator.SiteArtifacts;

/// <summary>
/// 生成 <c>feed.xml</c>（Atom 1.0）。详见 <c>Docs/Milestones/M3/M3.md §3.6</c>。
/// </summary>
public static class AtomFeedGenerator
{
    private const string Ns = "http://www.w3.org/2005/Atom";
    private const string StageName = "WriteSiteArtifactsStage";

    public static (BuildArtifact Artifact, ReadOnlyMemory<byte> Bytes) Build(
        ContentGraph.ContentGraph graph, DateTimeOffset generatedAt, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var posts = graph.Posts.Take(itemCount > 0 ? itemCount : 20).ToList();
        var host = graph.Site.NormalizedBaseUrl.Host;
        var feedUrl = SiteUrlResolver.Absolute(graph.Site.NormalizedBaseUrl, "/feed.xml").AbsoluteUri;
        var selfUrl = feedUrl;
        var siteUrl = graph.Site.NormalizedBaseUrl.AbsoluteUri;

        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "  ",
        };
        using (var writer = XmlWriter.Create(ms, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("feed", Ns);

            writer.WriteElementString("id", Ns, siteUrl);
            writer.WriteElementString("title", Ns, graph.Site.Settings.Title);
            if (!string.IsNullOrEmpty(graph.Site.Settings.Description))
            {
                writer.WriteElementString("subtitle", Ns, graph.Site.Settings.Description);
            }

            var feedUpdated = posts
                .Select(p => p.UpdatedAt ?? p.PublishedAt ?? generatedAt)
                .DefaultIfEmpty(generatedAt)
                .Max();
            writer.WriteElementString("updated", Ns, FormatUtc(feedUpdated));

            writer.WriteStartElement("link", Ns);
            writer.WriteAttributeString("rel", "self");
            writer.WriteAttributeString("type", "application/atom+xml");
            writer.WriteAttributeString("href", selfUrl);
            writer.WriteEndElement();

            writer.WriteStartElement("link", Ns);
            writer.WriteAttributeString("rel", "alternate");
            writer.WriteAttributeString("type", "text/html");
            writer.WriteAttributeString("href", siteUrl);
            writer.WriteEndElement();

            if (graph.Site.Settings.Author is { } author)
            {
                writer.WriteStartElement("author", Ns);
                writer.WriteElementString("name", Ns, author.Name);
                if (!string.IsNullOrEmpty(author.Email))
                {
                    writer.WriteElementString("email", Ns, author.Email);
                }

                writer.WriteEndElement();
            }

            writer.WriteElementString("generator", Ns, "Bocchi");

            foreach (var post in posts)
            {
                var published = post.PublishedAt ?? generatedAt;
                var updated = post.UpdatedAt ?? published;
                var entryUrl = SiteUrlResolver.Absolute(graph.Site.NormalizedBaseUrl, post.SiteRelativeUrl).AbsoluteUri;
                var tagId = string.Format(
                    CultureInfo.InvariantCulture,
                    "tag:{0},{1:yyyy-MM-dd}:posts/{2}/{3}",
                    host,
                    published.UtcDateTime,
                    post.Year,
                    post.Slug);

                writer.WriteStartElement("entry", Ns);
                writer.WriteElementString("id", Ns, tagId);
                writer.WriteElementString("title", Ns, post.Title);
                writer.WriteElementString("published", Ns, FormatUtc(published));
                writer.WriteElementString("updated", Ns, FormatUtc(updated));

                writer.WriteStartElement("link", Ns);
                writer.WriteAttributeString("rel", "alternate");
                writer.WriteAttributeString("type", "text/html");
                writer.WriteAttributeString("href", entryUrl);
                writer.WriteEndElement();

                foreach (var tag in post.Tags)
                {
                    writer.WriteStartElement("category", Ns);
                    writer.WriteAttributeString("term", tag);
                    writer.WriteEndElement();
                }

                if (!string.IsNullOrEmpty(post.Summary))
                {
                    writer.WriteStartElement("summary", Ns);
                    writer.WriteAttributeString("type", "text");
                    writer.WriteString(post.Summary);
                    writer.WriteEndElement();
                }

                writer.WriteStartElement("content", Ns);
                writer.WriteAttributeString("type", "html");
                writer.WriteString(post.BodyHtml);
                writer.WriteEndElement();

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        var bytes = ms.ToArray();
        var sha = HashUtil.Sha256Hex(bytes);
        return (
            new BuildArtifact
            {
                Path = "/feed.xml",
                Kind = ArtifactKind.SiteArtifact,
                ContentType = "application/atom+xml; charset=utf-8",
                SizeBytes = bytes.Length,
                Sha256 = sha,
                ProducedBy = StageName,
                Bytes = bytes,
            },
            bytes);
    }

    private static string FormatUtc(DateTimeOffset dt)
        => dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
