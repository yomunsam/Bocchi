using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Exceptions;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>把 <see cref="BuildSession.Scan"/> 加工成 <see cref="BuildSession.Graph"/>。</summary>
public sealed class BuildContentGraphStage : IBuildStage
{
    private readonly ContentGraphBuilder _builder;
    private readonly BocchiDataLayout _layout;

    public BuildContentGraphStage(ContentGraphBuilder builder, BocchiDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(layout);
        _builder = builder;
        _layout = layout;
    }

    public string Name => nameof(BuildContentGraphStage);

    public Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Scan is null)
        {
            throw new BuildPipelineException($"{Name} 之前必须先执行 LoadContentStage。");
        }

        var options = new ContentGraphOptions
        {
            IncludeDrafts = session.Options.IncludeDrafts,
            FailOnContentError = session.Options.FailOnContentError,
            FriendsDirectoryAbsolute = _layout.Workspace.FriendsDirectory,
        };
        var graph = _builder.Build(session.Scan, options);
        session.Graph = graph;
        session.Log(Name, BuildLogLevel.Info,
            $"内容图：posts={graph.Posts.Count}, pages={graph.Pages.Count}, works={graph.Works.Count}, notes={graph.Notes.Count}, friends={graph.Friends.Count}, media={graph.MediaAssets.Count}.");
        return Task.FromResult(true);
    }
}