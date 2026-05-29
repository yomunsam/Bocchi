using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>Theme staticAssets 的 manifest 校验与路径解析，供 Resolver、Package inspection 和复制阶段复用。</summary>
internal static class ThemeStaticAssetManifestValidator
{
    /// <summary>返回 staticAssets 声明中的阻断诊断；文件系统级 symlink 检查只覆盖源目录本身。</summary>
    public static IReadOnlyList<ThemeDiagnostic> Validate(ThemeManifest manifest, string themeRoot)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRoot);

        var diagnostics = new List<ThemeDiagnostic>();
        for (var i = 0; i < manifest.StaticAssets.Count; i++)
        {
            var asset = manifest.StaticAssets[i];
            ValidateFrom(asset.From, i, manifest.OutputDir, diagnostics);
            ValidateTo(asset.To, i, diagnostics);
            ValidatePatterns(asset.Include, "include", i, diagnostics);
            ValidatePatterns(asset.Exclude, "exclude", i, diagnostics);
            ValidateExistingSourceDirectory(asset.From, i, themeRoot, diagnostics);
        }

        return diagnostics;
    }

    /// <summary>解析一个已通过 manifest 校验的 staticAssets 源目录。</summary>
    public static string ResolveSourceDirectory(string themeRoot, ThemeStaticAssetManifest asset)
        => Path.GetFullPath(Path.Combine(Path.GetFullPath(themeRoot), NormalizeThemeRelativePath(asset.From)));

    /// <summary>把站点根相对目标目录规整为输出目录内的相对目录。</summary>
    public static string ResolveOutputRelativeDirectory(ThemeStaticAssetManifest asset)
        => string.Join(
            Path.DirectorySeparatorChar,
            SplitSitePath(asset.To).Select(part => part));

    private static void ValidateFrom(
        string? from,
        int index,
        string outputDir,
        List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(from))
        {
            diagnostics.Add(Error("theme-static-assets-from-empty", index, "staticAssets.from 不能为空。"));
            return;
        }

        if (Path.IsPathRooted(from))
        {
            diagnostics.Add(Error("theme-static-assets-from-rooted", index, $"staticAssets.from '{from}' 必须是相对 Theme Root 的目录。"));
            return;
        }

        if (!TryNormalizeThemeRelativePath(from, out var normalizedFrom, out var fromError))
        {
            diagnostics.Add(Error("theme-static-assets-from-invalid", index, fromError));
            return;
        }

        if (HasForbiddenSegment(normalizedFrom))
        {
            diagnostics.Add(Error("theme-static-assets-forbidden-dir", index, "staticAssets.from 不能指向 .git 或 node_modules。"));
        }

        if (TryNormalizeThemeRelativePath(outputDir, out var normalizedOutput, out _) &&
            PathsOverlap(normalizedFrom, normalizedOutput))
        {
            diagnostics.Add(Error("theme-static-assets-from-output", index, "staticAssets.from 不能指向或包含 Theme outputDir。"));
        }
    }

    private static void ValidateTo(string? to, int index, List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            diagnostics.Add(Error("theme-static-assets-to-empty", index, "staticAssets.to 不能为空。"));
            return;
        }

        if (!to.Trim().StartsWith('/'))
        {
            diagnostics.Add(Error("theme-static-assets-to-relative", index, $"staticAssets.to '{to}' 必须以 / 开头。"));
            return;
        }

        var parts = SplitSitePath(to);
        if (parts.Any(part => part is "." or ".."))
        {
            diagnostics.Add(Error("theme-static-assets-to-invalid", index, "staticAssets.to 不能包含 . 或 .. 路径段。"));
        }
    }

    private static void ValidatePatterns(
        IReadOnlyList<string>? patterns,
        string name,
        int index,
        List<ThemeDiagnostic> diagnostics)
    {
        if (patterns is null)
        {
            return;
        }

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                diagnostics.Add(Error("theme-static-assets-pattern-empty", index, $"staticAssets.{name} 不能包含空 glob。"));
            }
            else if (Path.IsPathRooted(pattern))
            {
                diagnostics.Add(Error("theme-static-assets-pattern-rooted", index, $"staticAssets.{name} glob '{pattern}' 必须相对 from 目录。"));
            }
        }
    }

    private static void ValidateExistingSourceDirectory(
        string? from,
        int index,
        string themeRoot,
        List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(from) ||
            Path.IsPathRooted(from) ||
            !TryNormalizeThemeRelativePath(from, out _, out _))
        {
            return;
        }

        var source = ResolveSourceDirectory(themeRoot, new ThemeStaticAssetManifest { From = from, To = "/" });
        if (!Directory.Exists(source))
        {
            diagnostics.Add(Error("theme-static-assets-source-missing", index, $"staticAssets.from '{from}' 指向的目录不存在。"));
            return;
        }

        if (!TryResolveLinkTarget(source, out var target))
        {
            return;
        }

        var root = Path.GetFullPath(themeRoot);
        if (!IsUnderRoot(target, root))
        {
            diagnostics.Add(Error("theme-static-assets-link-outside-root", index, $"staticAssets.from '{from}' 指向 Theme Root 外部。"));
        }
    }

    private static string NormalizeThemeRelativePath(string path)
    {
        if (!TryNormalizeThemeRelativePath(path, out var normalized, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return normalized;
    }

    private static bool TryNormalizeThemeRelativePath(string path, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var parts = path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "staticAssets.from 必须指向 Theme Root 下的目录。";
            return false;
        }

        if (parts.Any(part => part is "." or ".."))
        {
            error = "staticAssets.from 不能包含 . 或 .. 路径段。";
            return false;
        }

        normalized = string.Join('/', parts);
        return true;
    }

    private static string[] SplitSitePath(string path)
        => path.Trim().TrimStart('/')
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool HasForbiddenSegment(string normalizedPath)
        => normalizedPath.Split('/').Any(part =>
            string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(part, "node_modules", StringComparison.OrdinalIgnoreCase));

    private static bool PathsOverlap(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
            left.StartsWith(right + "/", StringComparison.OrdinalIgnoreCase) ||
            right.StartsWith(left + "/", StringComparison.OrdinalIgnoreCase);

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

    public static bool IsUnderRoot(string absolutePath, string root)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var rootWithSep = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        var normalized = Path.GetFullPath(absolutePath);
        return normalized.StartsWith(rootWithSep, StringComparison.Ordinal) ||
            string.Equals(normalized, normalizedRoot, StringComparison.Ordinal);
    }

    private static ThemeDiagnostic Error(string code, int index, string message)
        => new(ThemeDiagnosticSeverity.Error, code, $"staticAssets[{index}]: {message}");
}
