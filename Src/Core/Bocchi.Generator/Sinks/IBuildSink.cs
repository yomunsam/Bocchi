namespace Bocchi.Generator.Sinks;

/// <summary>
/// 构建产物的"写出端"。详见 <c>Docs/Milestones/M3/M3.md §3.2</c>。
/// </summary>
public interface IBuildSink
{
    /// <summary>把一个产物写出。Sink 自己负责打开文件 / HTTP 响应 / 内存断言。</summary>
    /// <param name="artifact">产物元信息。</param>
    /// <param name="content">产物字节流（可能来自内存，可能来自本地文件）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task WriteAsync(Pipeline.BuildArtifact artifact, Stream content, CancellationToken cancellationToken);
}
