using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Pipeline;
using Bocchi.Workspace;

namespace Bocchi.Generator.Sinks;

/// <summary>
/// 写入本地文件系统的 <see cref="IBuildSink"/>：根据 <see cref="ArtifactKind"/> 落到
/// <see cref="WorkspaceLayout.PublicOutputDirectory"/> 或 <see cref="WorkspaceLayout.ThemeInputDirectory"/>。
/// 写入前一律计算绝对路径并断言前缀，杜绝路径穿越。
/// </summary>
public sealed class FileSystemBuildSink : IBuildSink
{
    private readonly WorkspaceLayout _layout;
    private readonly string _publicRoot;
    private readonly string _inputRoot;

    public FileSystemBuildSink(WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _publicRoot = Path.GetFullPath(layout.PublicOutputDirectory);
        _inputRoot = Path.GetFullPath(layout.ThemeInputDirectory);
    }

    public string PublicRoot => _publicRoot;

    public string InputRoot => _inputRoot;

    /// <inheritdoc />
    public async Task WriteAsync(BuildArtifact artifact, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(content);

        var absolute = ResolveAbsolutePath(artifact);
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

    /// <summary>把 artifact 站点路径解析到绝对目标路径，并校验路径穿越。</summary>
    public string ResolveAbsolutePath(BuildArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var (root, rel) = ResolveRoot(artifact);
        var combined = Path.GetFullPath(Path.Combine(root, rel));
        EnsureUnder(combined, root, artifact.Path);
        return combined;
    }

    private (string Root, string Relative) ResolveRoot(BuildArtifact artifact)
    {
        var rel = NormalizeRelative(artifact.Path);
        return artifact.Kind switch
        {
            ArtifactKind.ThemeInput => (_inputRoot, rel),
            ArtifactKind.SiteArtifact => (_publicRoot, rel),
            ArtifactKind.Media => (_publicRoot, rel),
            ArtifactKind.ThemeOutput => (_publicRoot, rel),
            _ => throw new BuildPipelineException($"未知 ArtifactKind: {artifact.Kind}"),
        };
    }

    private static string NormalizeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BuildPipelineException("Artifact 路径不能为空。");
        }

        if (!path.StartsWith('/'))
        {
            throw new BuildPipelineException($"Artifact 路径必须以 '/' 开头，实际为：'{path}'");
        }

        var normalized = path[1..].Replace('/', Path.DirectorySeparatorChar);
        return normalized;
    }

    private static void EnsureUnder(string absolute, string root, string artifactPath)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(absolute, root, StringComparison.Ordinal))
        {
            throw new BuildPipelineException(
                $"Artifact 路径 '{artifactPath}' 解析出的目标 '{absolute}' 越过输出根 '{root}'。");
        }
    }
}