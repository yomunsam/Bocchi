using System.Reflection;

namespace Bocchi.Themes.BuiltIn.Bundle;

/// <summary>访问随程序集分发的默认 Theme 静态资源。</summary>
internal static class DefaultThemeBundleResources
{
    /// <summary>默认 Theme embedded resource 的统一前缀。</summary>
    private const string ResourcePrefix = "Bocchi.Themes.BuiltIn.Bundle.Theme/";

    /// <summary>承载默认 Theme 静态资源的程序集。</summary>
    private static readonly Assembly Assembly = typeof(DefaultThemeBundleResources).Assembly;

    /// <summary>
    /// 以标准正斜杠路径索引实际 manifest resource 名称。
    /// MSBuild 在不同平台可能保留不同目录分隔符，因此读取时不能重新拼接 resource 名称。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ResourceNames = Assembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .ToDictionary(
            name => NormalizeManifestPath(name[ResourcePrefix.Length..]),
            name => name,
            StringComparer.Ordinal);

    /// <summary>随包分发的默认 Theme 文件列表，路径相对 Theme 根目录。</summary>
    public static IReadOnlyList<string> Files { get; } = ResourceNames.Keys
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>复制一个默认 Theme 文件到目标路径。</summary>
    public static async Task CopyToFileAsync(
        string relativePath,
        string destination,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        await using var source = OpenRequired(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var target = new FileStream(
            destination,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>打开一个必须存在的默认 Theme 文件资源。</summary>
    private static Stream OpenRequired(string relativePath)
    {
        var normalized = NormalizeRequestedPath(relativePath);
        if (!ResourceNames.TryGetValue(normalized, out var resourceName))
        {
            throw new DefaultThemeBundleException($"默认 Theme embedded resource 缺失：'{normalized}'。");
        }

        return Assembly.GetManifestResourceStream(resourceName)
            ?? throw new DefaultThemeBundleException($"默认 Theme embedded resource 无法打开：'{normalized}'。");
    }

    /// <summary>把 manifest 中的平台相关分隔符统一为 Theme 使用的正斜杠路径。</summary>
    private static string NormalizeManifestPath(string relativePath)
        => relativePath.Replace('\\', '/').TrimStart('/');

    /// <summary>校验并标准化 Theme 内部相对路径，避免读取越过默认 Theme 根。</summary>
    private static string NormalizeRequestedPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = NormalizeManifestPath(relativePath);
        if (normalized.Split('/').Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new DefaultThemeBundleException($"默认 Theme resource 路径非法：'{relativePath}'。");
        }

        return normalized;
    }
}
