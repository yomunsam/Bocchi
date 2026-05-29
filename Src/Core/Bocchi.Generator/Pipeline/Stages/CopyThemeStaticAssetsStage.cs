using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Theme;
using Bocchi.GeneratorContract;

using Microsoft.Extensions.FileSystemGlobbing;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>按 Theme manifest 的 staticAssets 声明，把 Theme 源资源复制到 Theme 本地输出目录。</summary>
public sealed class CopyThemeStaticAssetsStage : IBuildStage
{
    public string Name => nameof(CopyThemeStaticAssetsStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var loaded = session.GetItem<LoadedTheme>(BuildSessionKeys.LoadedTheme);
        if (loaded is null)
        {
            session.Log(Name, BuildLogLevel.Info, "未加载 Theme：跳过 Theme staticAssets。");
            return true;
        }

        if (loaded.Manifest.StaticAssets.Count == 0)
        {
            session.Log(Name, BuildLogLevel.Info, $"Theme '{loaded.Manifest.Id}' 未声明 staticAssets。");
            return true;
        }

        if (!TryResolveOutputRoot(session, loaded, out var outputRoot))
        {
            session.Log(Name, BuildLogLevel.Info, "Live 模式未提供 Theme 输出目录：跳过 Theme staticAssets。");
            return true;
        }

        var diagnostics = ThemeStaticAssetManifestValidator.Validate(loaded.Manifest, loaded.ThemeRoot);
        if (diagnostics.Any(diagnostic => diagnostic.IsBlocking))
        {
            var summary = string.Join("; ", diagnostics.Where(diagnostic => diagnostic.IsBlocking).Select(diagnostic => diagnostic.Message));
            throw new BuildPipelineException($"Theme '{loaded.Manifest.Id}' staticAssets 声明非法：{summary}");
        }

        var copied = 0;
        foreach (var declaration in loaded.Manifest.StaticAssets)
        {
            copied += await CopyDeclarationAsync(loaded.ThemeRoot, outputRoot, declaration, session.CancellationToken)
                .ConfigureAwait(false);
        }

        session.Log(Name, BuildLogLevel.Info, $"已复制 Theme staticAssets {copied} 个文件。");
        return true;
    }

    private static async Task<int> CopyDeclarationAsync(
        string themeRoot,
        string outputRoot,
        ThemeStaticAssetManifest declaration,
        CancellationToken cancellationToken)
    {
        var sourceDirectory = ThemeStaticAssetManifestValidator.ResolveSourceDirectory(themeRoot, declaration);
        var outputRelativeDirectory = ThemeStaticAssetManifestValidator.ResolveOutputRelativeDirectory(declaration);
        var targetDirectory = Path.Combine(outputRoot, outputRelativeDirectory);
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<string> includes = declaration.Include is { Count: > 0 } ? declaration.Include : ["**/*"];
        matcher.AddIncludePatterns(includes);
        matcher.AddExcludePatterns(declaration.Exclude ?? []);
        matcher.AddExcludePatterns(["**/.git/**", "**/.git", "**/node_modules/**", "**/node_modules"]);

        var files = matcher.GetResultsInFullPath(sourceDirectory)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var copied = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDirectory, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            EnsureAllowedSourcePath(themeRoot, sourceDirectory, file, relativePath);
            var destination = Path.Combine(targetDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            EnsureDestinationAvailable(destination, outputRoot);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            copied++;
        }

        return copied;
    }

    private static void EnsureAllowedSourcePath(
        string themeRoot,
        string sourceDirectory,
        string file,
        string relativePath)
    {
        if (relativePath.Split('/').Any(part =>
                string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "node_modules", StringComparison.OrdinalIgnoreCase)))
        {
            throw new BuildPipelineException($"Theme staticAssets 禁止复制 .git 或 node_modules 下的文件：'{relativePath}'。");
        }

        var root = Path.GetFullPath(themeRoot);
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var fullFile = Path.GetFullPath(file);
        if (!ThemeStaticAssetManifestValidator.IsUnderRoot(fullFile, sourceRoot))
        {
            throw new BuildPipelineException($"Theme staticAssets 文件 '{file}' 不在 from 目录 '{sourceDirectory}' 下。");
        }

        EnsureReparsePointsStayInsideThemeRoot(root, sourceRoot, fullFile);
    }

    private static void EnsureReparsePointsStayInsideThemeRoot(string themeRoot, string sourceRoot, string file)
    {
        var relative = Path.GetRelativePath(sourceRoot, file)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = sourceRoot;
        foreach (var segment in relative)
        {
            current = Path.Combine(current, segment);
            if (!TryResolveLinkTarget(current, out var target))
            {
                continue;
            }

            if (!ThemeStaticAssetManifestValidator.IsUnderRoot(target, themeRoot))
            {
                throw new BuildPipelineException($"Theme staticAssets symlink/reparse point '{current}' 指向 Theme Root 外部。");
            }
        }
    }

    private static bool TryResolveLinkTarget(string path, out string target)
    {
        target = string.Empty;
        var attributes = File.GetAttributes(path);
        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        FileSystemInfo info = attributes.HasFlag(FileAttributes.Directory)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
        if (resolved is null)
        {
            return false;
        }

        target = Path.GetFullPath(resolved.FullName);
        return true;
    }

    private static void EnsureDestinationAvailable(string destination, string outputRoot)
    {
        var fullDestination = Path.GetFullPath(destination);
        if (!ThemeStaticAssetManifestValidator.IsUnderRoot(fullDestination, outputRoot))
        {
            throw new BuildPipelineException($"Theme staticAssets 目标 '{destination}' 越过 Theme 输出目录。");
        }

        if (File.Exists(fullDestination) || Directory.Exists(fullDestination))
        {
            throw new BuildPipelineException($"Theme staticAssets 目标 '{ToSiteRelativePath(outputRoot, fullDestination)}' 已存在。");
        }
    }

    /// <summary>解析 Theme 输出目录；Full Build 使用 Theme 本地 outputDir，Live 预览使用一次性输出目录。</summary>
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

        outputRoot = Path.GetFullPath(session.Options.LiveThemeOutputDirectory);
        return true;
    }

    private static string ToSiteRelativePath(string outputRoot, string destination)
        => "/" + Path.GetRelativePath(outputRoot, destination)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
}
