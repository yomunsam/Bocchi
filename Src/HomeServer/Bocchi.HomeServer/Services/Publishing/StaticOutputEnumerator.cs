using System.Globalization;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>发布时看到的单个静态输出文件。</summary>
public sealed record StaticOutputFile
{
    /// <summary>相对站点根的 POSIX 路径，不以斜杠开头。</summary>
    public required string RelativePath { get; init; }

    /// <summary>文件绝对路径。</summary>
    public required string AbsolutePath { get; init; }

    /// <summary>文件大小。</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>发布前对 <c>output/public/</c> 的稳定快照。</summary>
public sealed record StaticOutputSnapshot
{
    /// <summary>静态输出根目录绝对路径。</summary>
    public required string RootDirectory { get; init; }

    /// <summary>按路径排序后的文件列表。</summary>
    public required IReadOnlyList<StaticOutputFile> Files { get; init; }

    /// <summary>总字节数。</summary>
    public long TotalSizeBytes => Files.Sum(x => x.SizeBytes);
}

/// <summary>枚举本地静态输出目录，并做发布前路径与文件大小校验。</summary>
public static class StaticOutputEnumerator
{
    /// <summary>GitHub Git blob API 的单文件大小上限；超过后需要未来引入其他上传策略。</summary>
    public const long GitHubBlobSizeLimitBytes = 100L * 1024L * 1024L;

    /// <summary>读取静态输出快照；目录不存在、为空或路径异常时直接失败。</summary>
    public static StaticOutputSnapshot Enumerate(string publicRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicRoot);

        var root = Path.GetFullPath(publicRoot);
        if (!Directory.Exists(root))
        {
            throw new PublishTargetException("静态输出目录不存在，请先生成静态站点。");
        }

        var files = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => CreateFile(root, file))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new PublishTargetException("静态输出目录为空，请先生成静态站点。");
        }

        return new StaticOutputSnapshot
        {
            RootDirectory = root,
            Files = files,
        };
    }

    /// <summary>把文件系统路径转换为发布路径，并阻止路径穿越和过大的 blob。</summary>
    private static StaticOutputFile CreateFile(string root, string file)
    {
        var absolute = Path.GetFullPath(file);
        EnsureUnderRoot(absolute, root);

        var info = new FileInfo(absolute);
        if (info.Length > GitHubBlobSizeLimitBytes)
        {
            var size = info.Length.ToString("N0", CultureInfo.InvariantCulture);
            throw new PublishTargetException($"静态输出文件 '{Path.GetFileName(absolute)}' 大小为 {size} bytes，超过 GitHub 单文件发布上限。");
        }

        var relative = Path.GetRelativePath(root, absolute).Replace(Path.DirectorySeparatorChar, '/');
        if (string.IsNullOrWhiteSpace(relative)
            || relative.StartsWith("../", StringComparison.Ordinal)
            || relative.Contains("/../", StringComparison.Ordinal)
            || relative.EndsWith("/..", StringComparison.Ordinal))
        {
            throw new PublishTargetException($"静态输出文件路径不安全：{relative}");
        }

        return new StaticOutputFile
        {
            RelativePath = relative,
            AbsolutePath = absolute,
            SizeBytes = info.Length,
        };
    }

    /// <summary>校验文件仍在输出根目录下。</summary>
    private static void EnsureUnderRoot(string absolute, string root)
    {
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(absolute, root, StringComparison.Ordinal))
        {
            throw new PublishTargetException($"静态输出文件 '{absolute}' 不在输出根 '{root}' 下。");
        }
    }
}
