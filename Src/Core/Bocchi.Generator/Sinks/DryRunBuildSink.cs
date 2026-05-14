using Bocchi.Generator.Pipeline;

namespace Bocchi.Generator.Sinks;

/// <summary>仅断言、不写文件的 Sink，用于测试 / dry-run。</summary>
public sealed class DryRunBuildSink : IBuildSink
{
    private readonly List<BuildArtifact> _captured = [];

    public IReadOnlyList<BuildArtifact> CapturedArtifacts => _captured;

    public Task WriteAsync(BuildArtifact artifact, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(content);
        // 仍然消耗流以模拟真实 Sink 的语义
        return DrainAsync(artifact, content, cancellationToken);
    }

    private async Task DrainAsync(BuildArtifact artifact, Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        while (await content.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        _captured.Add(artifact);
    }
}