using Bocchi.Generator.Exceptions;
using Bocchi.Generator.State;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>把 artifact 写入 Sink 并登记到 <see cref="BuildSession"/> 与 <see cref="IBuildStateStore"/>。</summary>
internal static class ArtifactSinkHelper
{
    public static async Task WriteAsync(BuildSession session, BuildArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(artifact);

        // 在 Live 模式下：若有 OnlyArtifactPath 过滤，则仅匹配的写到 Sink，其它仍登记到状态/会话
        if (ShouldWriteToSink(session, artifact))
        {
            await using var stream = OpenStream(artifact);
            await session.Sink.WriteAsync(artifact, stream, session.CancellationToken).ConfigureAwait(false);
        }

        session.RecordArtifact(artifact);
        if (session.BuildRunId is { } runId)
        {
            var store = session.GetItem<IBuildStateStore>("buildStateStore")
                ?? throw new BuildPipelineException("BuildStateStore 未注入 BuildSession 上下文。");
            await store.RecordArtifactAsync(runId, artifact, session.CancellationToken).ConfigureAwait(false);
        }
    }

    private static bool ShouldWriteToSink(BuildSession session, BuildArtifact artifact)
    {
        if (string.IsNullOrEmpty(session.Options.OnlyArtifactPath))
        {
            return true;
        }

        return string.Equals(session.Options.OnlyArtifactPath, artifact.Path, StringComparison.Ordinal);
    }

    private static Stream OpenStream(BuildArtifact artifact)
    {
        if (artifact.Bytes is { } mem)
        {
            return new MemoryStream(mem.ToArray(), writable: false);
        }

        if (!string.IsNullOrEmpty(artifact.SourceAbsolutePath))
        {
            return new FileStream(
                artifact.SourceAbsolutePath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
        }

        throw new BuildPipelineException($"Artifact '{artifact.Path}' 既没有 Bytes，也没有 SourceAbsolutePath。");
    }
}
