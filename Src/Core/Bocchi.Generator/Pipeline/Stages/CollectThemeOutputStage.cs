using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Theme;
using Bocchi.Generator.Utilities;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>把 Theme 本地输出目录中的 HTML / CSS / JS / assets 收集为 <see cref="ArtifactKind.ThemeOutput"/>。</summary>
public sealed class CollectThemeOutputStage : IBuildStage
{
    public string Name => nameof(CollectThemeOutputStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var loaded = session.GetItem<LoadedTheme>(BuildSessionKeys.LoadedTheme);
        if (loaded is null)
        {
            session.Log(Name, BuildLogLevel.Info, "未加载 Theme：跳过 Theme 输出收集。");
            return true;
        }

        if (!TryResolveOutputRoot(session, loaded, out var outputRoot))
        {
            session.Log(Name, BuildLogLevel.Info, "Live 模式未提供 Theme 输出目录：跳过 Theme 输出收集。");
            return true;
        }

        if (!Directory.Exists(outputRoot))
        {
            session.Log(Name, BuildLogLevel.Warning, $"Theme '{loaded.Manifest.Id}' 未生成输出目录：{outputRoot}");
            return true;
        }

        var reservedPaths = session.Artifacts
            .Where(a => a.Kind is ArtifactKind.SiteArtifact or ArtifactKind.Media)
            .Select(a => a.Path)
            .Append(WriteManifestStage.ManifestPath)
            .ToHashSet(StringComparer.Ordinal);

        var files = Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
            .Select(file => new CollectedThemeFile(file, ToArtifactPath(outputRoot, file)))
            .OrderBy(file => file.ArtifactPath, StringComparer.Ordinal)
            .ToArray();

        foreach (var file in files)
        {
            if (reservedPaths.Contains(file.ArtifactPath))
            {
                throw new BuildPipelineException($"Theme 输出 '{file.ArtifactPath}' 与已有站点产物冲突。");
            }

            var info = new FileInfo(file.AbsolutePath);
            var artifact = new BuildArtifact
            {
                Path = file.ArtifactPath,
                Kind = ArtifactKind.ThemeOutput,
                ContentType = ResolveContentType(file.ArtifactPath),
                SizeBytes = info.Length,
                Sha256 = await Sha256Hex.FromFileAsync(file.AbsolutePath, session.CancellationToken).ConfigureAwait(false),
                ProducedBy = Name,
                SourceAbsolutePath = file.AbsolutePath,
            };
            await ArtifactSinkHelper.WriteAsync(session, artifact).ConfigureAwait(false);
        }

        session.Log(Name, BuildLogLevel.Info, $"已收集 Theme 输出 {files.Length} 个文件。");
        return true;
    }

    /// <summary>解析 Theme 输出收集目录；Live 预览只收集一次性输出目录。</summary>
    private static bool TryResolveOutputRoot(BuildSession session, LoadedTheme loaded, out string outputRoot)
    {
        if (session.Options.Mode != BuildMode.Live)
        {
            outputRoot = ThemeOutputPathResolver.ResolveLocalOutputDirectory(loaded.ThemeRoot, loaded.Manifest.OutputDir);
            return true;
        }

        if (string.IsNullOrWhiteSpace(session.Options.LiveThemeOutputDirectory))
        {
            outputRoot = string.Empty;
            return false;
        }

        // Live 预览收集的是一次性 Theme 输出目录，不能回读 Full Build 的 output/public。
        outputRoot = Path.GetFullPath(session.Options.LiveThemeOutputDirectory);
        return true;
    }

    private static string ToArtifactPath(string outputRoot, string file)
    {
        var root = Path.GetFullPath(outputRoot);
        var absolute = Path.GetFullPath(file);
        if (!IsUnderRoot(absolute, root))
        {
            throw new BuildPipelineException($"Theme 输出文件 '{absolute}' 不在输出目录 '{root}' 下。");
        }

        var relative = Path.GetRelativePath(root, absolute)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        return "/" + relative;
    }

    private static bool IsUnderRoot(string absolute, string root)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return absolute.StartsWith(rootWithSep, StringComparison.Ordinal) ||
               string.Equals(absolute, root, StringComparison.Ordinal);
    }

    private static string ResolveContentType(string artifactPath)
    {
        return Path.GetExtension(artifactPath).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream",
        };
    }

    private sealed record CollectedThemeFile(string AbsolutePath, string ArtifactPath);
}
