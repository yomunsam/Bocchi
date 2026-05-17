using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;

namespace Bocchi.HomeServer.Build;

/// <summary>
/// Home Server 实时预览专用 Sink：把 Theme 输入写到一次性目录，同时只捕获当前请求需要返回的 artifact。
/// </summary>
internal sealed class LivePreviewBuildSink : IBuildSink
{
    private readonly string _themeInputRoot;
    private byte[]? _matchedBytes;

    /// <summary>构造实时预览 Sink。</summary>
    /// <param name="themeInputDirectory">本次预览专用 Theme input 目录。</param>
    /// <param name="expectedArtifactPath">本次 HTTP 响应要返回的 artifact 路径，例如 <c>/index.html</c>。</param>
    public LivePreviewBuildSink(string themeInputDirectory, string expectedArtifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeInputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArtifactPath);
        if (!expectedArtifactPath.StartsWith('/'))
        {
            throw new ArgumentException("Expected artifact path must start with '/'.", nameof(expectedArtifactPath));
        }

        _themeInputRoot = Path.GetFullPath(themeInputDirectory);
        ExpectedArtifactPath = expectedArtifactPath;
    }

    /// <summary>本次请求要返回的 artifact 路径。</summary>
    public string ExpectedArtifactPath { get; }

    /// <summary>命中的 artifact 元数据；为空表示 Pipeline 没有生成目标路径。</summary>
    public BuildArtifact? MatchedArtifact { get; private set; }

    /// <summary>是否已经捕获到目标 artifact。</summary>
    public bool Matched => MatchedArtifact is not null;

    /// <inheritdoc />
    public async Task WriteAsync(BuildArtifact artifact, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(content);

        if (artifact.Kind == ArtifactKind.ThemeInput)
        {
            await WriteThemeInputAsync(artifact, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(artifact.Path, ExpectedArtifactPath, StringComparison.Ordinal))
        {
            await DrainAsync(content, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        _matchedBytes = buffer.ToArray();
        MatchedArtifact = artifact;
    }

    /// <summary>返回已捕获 artifact 的字节；调用方应先检查 <see cref="Matched"/>。</summary>
    public byte[] GetMatchedBytes() => _matchedBytes ?? [];

    /// <summary>把 Theme Contract 输入写入本次 Live build 的临时 input 目录。</summary>
    private async Task WriteThemeInputAsync(BuildArtifact artifact, Stream content, CancellationToken cancellationToken)
    {
        var absolute = ResolveThemeInputPath(artifact.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await using var output = new FileStream(
            absolute,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把 artifact 路径解析为临时 input 目录下的绝对文件路径。</summary>
    private string ResolveThemeInputPath(string artifactPath)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !artifactPath.StartsWith('/'))
        {
            throw new InvalidOperationException($"Theme input artifact path '{artifactPath}' is invalid.");
        }

        var relative = artifactPath[1..].Replace('/', Path.DirectorySeparatorChar);
        var absolute = Path.GetFullPath(Path.Combine(_themeInputRoot, relative));
        EnsureUnderThemeInputRoot(absolute, artifactPath);
        return absolute;
    }

    /// <summary>确认 Theme input artifact 不会越过本次预览的临时 input 根目录。</summary>
    private void EnsureUnderThemeInputRoot(string absolute, string artifactPath)
    {
        var rootWithSep = _themeInputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _themeInputRoot
            : _themeInputRoot + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(absolute, _themeInputRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Theme input artifact '{artifactPath}' escapes live preview input root.");
        }
    }

    /// <summary>消耗非目标 artifact 的流，保持 Sink 语义完整。</summary>
    private static async Task DrainAsync(Stream content, CancellationToken cancellationToken)
    {
        var discard = new byte[8 * 1024];
        while (await content.ReadAsync(discard.AsMemory(), cancellationToken).ConfigureAwait(false) > 0)
        {
        }
    }
}
