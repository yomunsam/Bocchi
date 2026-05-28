using System.Text;

using Bocchi.ContentModel;
using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Utilities;

namespace Bocchi.Generator.SiteArtifacts;

/// <summary>生成 <c>robots.txt</c>。</summary>
public static class RobotsTxtGenerator
{
    private const string StageName = "WriteSiteArtifactsStage";

    public static (BuildArtifact Artifact, ReadOnlyMemory<byte> Bytes) Build(GraphSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var sb = new StringBuilder();
        sb.Append("User-agent: *\n");
        foreach (var allow in site.Settings.Robots.Allow)
        {
            sb.Append("Allow: ").Append(allow).Append('\n');
        }

        foreach (var disallow in site.Settings.Robots.Disallow)
        {
            sb.Append("Disallow: ").Append(disallow).Append('\n');
        }

        if (site.Settings.EnableSitemap)
        {
            sb.Append("Sitemap: ").Append(SiteUrlResolver.Absolute(site.NormalizedBaseUrl, "/sitemap.xml")).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var sha = Sha256Hex.FromBytes(bytes);
        return (
            new BuildArtifact
            {
                Path = "/robots.txt",
                Kind = ArtifactKind.SiteArtifact,
                ContentType = "text/plain; charset=utf-8",
                SizeBytes = bytes.Length,
                Sha256 = sha,
                ProducedBy = StageName,
                Bytes = bytes,
            },
            bytes);
    }
}
