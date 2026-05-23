using System.Reflection;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>访问随程序集分发的默认 Theme 源文件。</summary>
internal static class DefaultStaticThemeResources
{
    /// <summary>默认 Theme embedded resource 的统一前缀。</summary>
    private const string ResourcePrefix = "Bocchi.Theme.DefaultStatic.DefaultTheme/";

    /// <summary>承载默认 Theme 源文件资源的程序集。</summary>
    private static readonly Assembly Assembly = typeof(DefaultStaticThemeResources).Assembly;

    /// <summary>随包分发的默认 Theme 文件列表，路径相对 Theme 根目录。</summary>
    public static IReadOnlyList<string> Files { get; } = Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..].Replace('\\', '/'))
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Order(StringComparer.Ordinal)
        .ToArray();

    /// <summary>复制一个默认 Theme 源文件到目标路径。</summary>
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

    /// <summary>按 UTF-8 读取一个默认 Theme 文本源文件；资源缺失时返回 null。</summary>
    public static async Task<string?> TryReadTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var stream = Open(relativePath);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>打开一个默认 Theme 源文件资源；资源缺失时返回 null。</summary>
    public static Stream? Open(string relativePath)
    {
        var normalized = Normalize(relativePath);
        return Assembly.GetManifestResourceStream(ResourcePrefix + normalized);
    }

    /// <summary>打开一个必须存在的默认 Theme 源文件资源。</summary>
    private static Stream OpenRequired(string relativePath)
    {
        var normalized = Normalize(relativePath);
        return Assembly.GetManifestResourceStream(ResourcePrefix + normalized)
            ?? throw new DefaultStaticThemeException($"默认 Theme embedded resource 缺失：'{normalized}'。");
    }

    /// <summary>清理 Theme 内部相对路径，避免资源读取越过默认 Theme 根。</summary>
    private static string Normalize(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(normalized))
        {
            throw new DefaultStaticThemeException($"默认 Theme resource 路径非法：'{relativePath}'。");
        }

        return normalized;
    }
}
