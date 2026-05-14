using Bocchi.Generator.Exceptions;
using Bocchi.Generator.SiteArtifacts;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>生成 robots.txt / sitemap.xml / feed.xml 并写到 Sink。</summary>
public sealed class WriteSiteArtifactsStage : IBuildStage
{
    private readonly TimeProvider _time;

    public WriteSiteArtifactsStage(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    public string Name => nameof(WriteSiteArtifactsStage);

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

        session.Log(Name, BuildLogLevel.Info, "robots.txt / sitemap.xml / feed.xml 已写入。");
        return true;
    }
}
