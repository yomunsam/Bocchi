using Bocchi.Generator.Pipeline;

namespace Bocchi.Generator.Sinks;

/// <summary>
/// 在实时模式下使用：把 artifact 流式写入到一个一次性 HTTP 响应。
/// 由 HomeServer 预览端点构造 + 注入；典型用法是"<see cref="ExpectedArtifactPath"/> 命中后写入响应、再终止 Pipeline"。
/// </summary>
public sealed class HttpStreamBuildSink : IBuildSink
{
    private readonly Stream _output;
    private readonly Action<BuildArtifact>? _onMatched;

    /// <summary>
    /// 构造一个 HTTP 流式 Sink。
    /// </summary>
    /// <param name="output">输出流（通常为 <c>HttpResponse.Body</c>）。</param>
    /// <param name="expectedArtifactPath">期望吐出的 artifact 站点根相对路径；其它 artifact 直接丢弃。</param>
    /// <param name="onMatched">命中 expected 时回调，可用于设置 HTTP headers（Content-Type / ETag）。</param>
    public HttpStreamBuildSink(Stream output, string expectedArtifactPath, Action<BuildArtifact>? onMatched = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArtifactPath);
        _output = output;
        ExpectedArtifactPath = expectedArtifactPath;
        _onMatched = onMatched;
    }

    public string ExpectedArtifactPath { get; }

    public bool Matched { get; private set; }

    public async Task WriteAsync(BuildArtifact artifact, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(content);
        if (!string.Equals(artifact.Path, ExpectedArtifactPath, StringComparison.Ordinal))
        {
            // 丢弃但仍消耗流，避免上游死锁
            var discard = new byte[8 * 1024];
            while (await content.ReadAsync(discard.AsMemory(), cancellationToken).ConfigureAwait(false) > 0)
            {
            }

            return;
        }

        Matched = true;
        _onMatched?.Invoke(artifact);
        await content.CopyToAsync(_output, cancellationToken).ConfigureAwait(false);
    }
}
