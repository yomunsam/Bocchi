using Bocchi.Generator.Exceptions;
using Bocchi.Generator.SiteArtifacts;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>生成 robots.txt / sitemap.xml / feed.xml / .nojekyll 并写到 Sink。</summary>
public sealed class WriteSiteArtifactsStage : IBuildStage
{
    /// <summary>.nojekyll 文件内容；保留换行以满足站点产物非空校验。</summary>
    private static readonly byte[] NoJekyllBytes = "\n"u8.ToArray();

    /// <summary>时间来源，传递给按时间生成的站点产物。</summary>
    private readonly TimeProvider _time;

    /// <summary>构造站点产物写入阶段。</summary>
    public WriteSiteArtifactsStage(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <inheritdoc />
    public string Name => nameof(WriteSiteArtifactsStage);

    /// <inheritdoc />
    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Graph is null)
        {
            throw new BuildPipelineException($"{Name} 需要内容图。");
        }

        var graph = session.Graph;

        var (robotsArtifact, _) = RobotsTxtGenerator.Build(graph.Site);
        await ArtifactSinkHelper.WriteAsync(session, robotsArtifact).ConfigureAwait(false);

        await ArtifactSinkHelper.WriteAsync(session, CreateNoJekyllArtifact()).ConfigureAwait(false);

        if (graph.Site.Settings.EnableSitemap)
        {
            var (sitemapArtifact, _) = SitemapXmlGenerator.Build(graph, _ => null);
            await ArtifactSinkHelper.WriteAsync(session, sitemapArtifact).ConfigureAwait(false);
        }

        if (graph.Site.Settings.EnableRss)
        {
            var feedCount = session.Options.FeedItemCount ?? graph.Site.Settings.FeedItemCount;
            var (feedArtifact, _) = AtomFeedGenerator.Build(graph, _time.GetUtcNow(), feedCount);
            await ArtifactSinkHelper.WriteAsync(session, feedArtifact).ConfigureAwait(false);
        }

        session.Log(Name, BuildLogLevel.Info, "robots.txt / sitemap.xml / feed.xml / .nojekyll 已写入。");
        return true;
    }

    /// <summary>生成 GitHub Pages 所需的 .nojekyll 标记文件；保留一个换行以通过站点产物非空校验。</summary>
    private BuildArtifact CreateNoJekyllArtifact()
        => new()
        {
            Path = "/.nojekyll",
            Kind = ArtifactKind.SiteArtifact,
            ContentType = "text/plain; charset=utf-8",
            SizeBytes = NoJekyllBytes.Length,
            Sha256 = Sha256Util.Hex(NoJekyllBytes),
            ProducedBy = Name,
            Bytes = NoJekyllBytes,
        };
}
