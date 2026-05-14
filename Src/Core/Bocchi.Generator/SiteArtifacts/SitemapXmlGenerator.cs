using System.Globalization;
using System.Text;
using System.Xml;

using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Pipeline;

namespace Bocchi.Generator.SiteArtifacts;

/// <summary>
/// 生成 <c>sitemap.xml</c>（XML Sitemap 0.9）。
/// </summary>
public static class SitemapXmlGenerator
{
    private const string Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string StageName = "WriteSiteArtifactsStage";

    public static (BuildArtifact Artifact, ReadOnlyMemory<byte> Bytes) Build(
        ContentGraph.ContentGraph graph, Func<string, DateTimeOffset?> sourceMtimeProvider)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sourceMtimeProvider);

        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "  ",
            NewLineOnAttributes = false,
        };
        using (var writer = XmlWriter.Create(ms, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", Ns);

            void WriteUrl(string siteRel, DateTimeOffset? lastMod)
            {
                writer.WriteStartElement("url", Ns);
                writer.WriteElementString("loc", Ns, SiteUrlResolver.Absolute(graph.Site.NormalizedBaseUrl, siteRel).AbsoluteUri);
                if (lastMod is { } lm)
                {
                    writer.WriteElementString("lastmod", Ns, lm.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                }

                writer.WriteEndElement();
            }

            // 首页（约定 / 对应 pages/index 或站点根）
            WriteUrl("/", null);

            foreach (var page in graph.Pages)
            {
                WriteUrl(page.SiteRelativeUrl, sourceMtimeProvider($"pages/{page.Slug}"));
            }

            foreach (var post in graph.Posts)
            {
                var mtime = post.UpdatedAt ?? post.PublishedAt ?? sourceMtimeProvider($"posts/{post.Year}/{post.Slug}");
                WriteUrl(post.SiteRelativeUrl, mtime);
            }

            foreach (var work in graph.Works)
            {
                var mtime = sourceMtimeProvider($"works/{work.Year}/{work.Slug}");
                WriteUrl(work.SiteRelativeUrl, mtime);
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        var bytes = ms.ToArray();
        var sha = HashUtil.Sha256Hex(bytes);
        return (
            new BuildArtifact
            {
                Path = "/sitemap.xml",
                Kind = ArtifactKind.SiteArtifact,
                ContentType = "application/xml; charset=utf-8",
                SizeBytes = bytes.Length,
                Sha256 = sha,
                ProducedBy = StageName,
                Bytes = bytes,
            },
            bytes);
    }
}