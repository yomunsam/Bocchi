using Bocchi.Generator.Exceptions;

namespace Bocchi.Generator.Theme;

/// <summary>解析并维护 Theme 本地输出目录，保证构建输出不会越过 Theme 根目录。</summary>
internal static class ThemeOutputPathResolver
{
    /// <summary>把 <c>theme.json.outputDir</c> 解析成 Theme 根目录下的绝对路径。</summary>
    public static string ResolveLocalOutputDirectory(string themeRoot, string outputDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        if (Path.IsPathRooted(outputDir))
        {
            throw new BuildPipelineException($"Theme outputDir 必须是相对路径，实际为：'{outputDir}'。");
        }

        var root = Path.GetFullPath(themeRoot);
        var absolute = Path.GetFullPath(Path.Combine(root, outputDir));
        EnsureUnderRoot(absolute, root, outputDir);
        if (string.Equals(absolute, root, StringComparison.Ordinal))
        {
            throw new BuildPipelineException("Theme outputDir 不能指向 Theme 根目录。");
        }

        return absolute;
    }

    /// <summary>清空并重建 Theme 本地输出目录，避免旧输出被误收集到本次构建。</summary>
    public static void ResetLocalOutputDirectory(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);
    }

    private static void EnsureUnderRoot(string absolute, string root, string original)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(absolute, root, StringComparison.Ordinal))
        {
            throw new BuildPipelineException($"Theme outputDir '{original}' 解析后越过 Theme 根目录 '{root}'。");
        }
    }
}
