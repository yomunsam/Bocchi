using Bocchi.Generator.Exceptions;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>把所有 <see cref="ContentGraph.MediaAsset"/> 复制到 <c>output/public/media/...</c>（通过 Sink）。</summary>
public sealed class CopyMediaStage : IBuildStage
{
    public string Name => nameof(CopyMediaStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Graph is null)
        {
            throw new BuildPipelineException($"{Name} 需要内容图。");
        }

        long copied = 0;
        foreach (var asset in session.Graph.MediaAssets)
        {
            var artifact = new BuildArtifact
            {
                Path = asset.SiteRelativePath,
                Kind = ArtifactKind.Media,
                ContentType = asset.ContentType,
                SizeBytes = asset.SizeBytes,
                Sha256 = asset.Sha256,
                ProducedBy = Name,
                SourceAbsolutePath = asset.SourceAbsolutePath,
            };
            await ArtifactSinkHelper.WriteAsync(session, artifact).ConfigureAwait(false);
            copied++;
        }

        session.Log(Name, BuildLogLevel.Info, $"已复制媒体资源 {copied} 个。");
        return true;
    }
}