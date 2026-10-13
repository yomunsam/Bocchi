using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>把 <c>output/public/</c> 镜像同步到本机某个绝对目录。</summary>
public sealed class LocalDirectoryPublisher : IPublishTargetPublisher
{
    /// <summary>目标目录里的归属标记；有合法标记才允许自动删掉多余文件。</summary>
    public const string MarkerFileName = ".bocchi-local-publish.json";

    /// <summary>同时只允许一个本地目录镜像在进行，避免并发互删。</summary>
    private static readonly SemaphoreSlim PublishLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly BocchiDataLayout _layout;

    public LocalDirectoryPublisher(BocchiDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <inheritdoc />
    public string Channel => PublishPlanService.LocalDirectoryChannel;

    /// <inheritdoc />
    public async Task<PublishTargetResult> PublishAsync(PublishTargetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = LocalDirectoryPublishConfiguration.FromJson(request.ConfigurationJson).Normalize();
        var targetRoot = ValidateAndResolveTarget(configuration);

        await PublishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return PublishUnderLock(request, configuration, targetRoot, cancellationToken);
        }
        finally
        {
            PublishLock.Release();
        }
    }

    private PublishTargetResult PublishUnderLock(
        PublishTargetRequest request,
        LocalDirectoryPublishConfiguration configuration,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        // 进入锁后再解析一次，避免等待期间 symlink 被换掉
        targetRoot = ValidateAndResolveTarget(configuration);

        Directory.CreateDirectory(targetRoot);
        EnsureTargetIsSafeToMirror(targetRoot, configuration.AllowUnmarkedTarget);

        var sourceFiles = request.Output.Files;
        var relativeSet = new HashSet<string>(sourceFiles.Select(f => f.RelativePath), StringComparer.Ordinal);

        foreach (var file in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(file.RelativePath)
                || file.RelativePath.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(file.RelativePath))
            {
                throw new PublishTargetException($"本地发布拒绝不安全的相对路径：{file.RelativePath}");
            }

            var destination = Path.GetFullPath(
                Path.Combine(targetRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsStrictlyUnderOrEqualFile(destination, targetRoot))
            {
                throw new PublishTargetException($"本地发布目标路径逃逸出方案目录：{file.RelativePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file.AbsolutePath, destination, overwrite: true);
        }

        foreach (var existing in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(targetRoot, existing).Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(relative, MarkerFileName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!relativeSet.Contains(relative))
            {
                File.Delete(existing);
            }
        }

        PruneEmptyDirectories(targetRoot);

        var marker = new LocalPublishMarker(
            SchemaVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            FileCount: sourceFiles.Count,
            BuildFingerprint: request.BuildResult?.Fingerprint?.Value,
            Files: relativeSet.Order(StringComparer.Ordinal).ToArray());
        var markerJson = JsonSerializer.Serialize(marker, JsonOptions);
        File.WriteAllText(Path.Combine(targetRoot, MarkerFileName), markerJson + "\n", Encoding.UTF8);

        return new PublishTargetResult
        {
            RemoteCommitSha = null,
            RemoteUrl = new Uri(targetRoot).AbsoluteUri,
        };
    }

    /// <summary>
    /// 路径必须是绝对路径；解析符号链接最终目标后，不能指向文件系统根，也不能落在 DataRoot 整棵树内。
    /// </summary>
    private string ValidateAndResolveTarget(LocalDirectoryPublishConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.TargetDirectory))
        {
            throw new PublishTargetException("本地发布目录不能为空。");
        }

        if (!Path.IsPathRooted(configuration.TargetDirectory))
        {
            throw new PublishTargetException("本地发布目录必须是绝对路径。");
        }

        string target;
        try
        {
            target = Path.GetFullPath(configuration.TargetDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PublishTargetException($"本地发布目录无效：{ex.Message}");
        }

        target = ResolveFinalPath(target);

        if (IsFilesystemRoot(target))
        {
            throw new PublishTargetException("不能把站点发布到文件系统根目录。");
        }

        var dataRoot = Path.GetFullPath(_layout.DataRoot);
        if (IsUnder(target, dataRoot))
        {
            throw new PublishTargetException(
                "本地发布目录不能位于 Bocchi DataRoot 之内（含 backups、output、cache、logs、workspace 等）。请另选 DataRoot 以外的目录。");
        }

        return target;
    }

    /// <summary>
    /// 解析路径上的符号链接/junction。目录尚不存在时，解析已存在的最长前缀再拼回后缀。
    /// </summary>
    internal static string ResolveFinalPath(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        if (FileSystemEntryExists(normalized))
        {
            return ResolveExistingEntry(normalized);
        }

        var suffix = new Stack<string>();
        var current = normalized;
        while (!string.IsNullOrEmpty(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || PathsEqual(parent, current))
            {
                break;
            }

            suffix.Push(Path.GetFileName(current));
            if (FileSystemEntryExists(parent))
            {
                var resolvedParent = ResolveExistingEntry(parent);
                foreach (var segment in suffix)
                {
                    resolvedParent = Path.Combine(resolvedParent, segment);
                }

                return Path.GetFullPath(resolvedParent);
            }

            current = parent;
        }

        return normalized;
    }

    /// <summary>路径本身或祖先是否存在（含尚未解析的符号链接）。</summary>
    private static bool FileSystemEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string ResolveExistingEntry(string path)
    {
        FileSystemInfo info;
        try
        {
            var attrs = File.GetAttributes(path);
            // 符号链接（含断链）用 FileInfo 解析；普通目录用 DirectoryInfo
            info = (attrs & FileAttributes.ReparsePoint) != 0 || (attrs & FileAttributes.Directory) == 0
                ? new FileInfo(path)
                : new DirectoryInfo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PublishTargetException($"无法读取本地发布目录：{path}（{ex.Message}）");
        }

        try
        {
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                return Path.GetFullPath(target.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new PublishTargetException(
                $"无法解析本地发布目录的符号链接（可能已损坏、成环或无权访问）：{path}（{ex.Message}）");
        }

        return Path.GetFullPath(path);
    }

    /// <summary>非空目标必须有可解析的合法标记；损坏或假标记视为无标记。</summary>
    private static void EnsureTargetIsSafeToMirror(string targetRoot, bool allowUnmarked)
    {
        var markerPath = Path.Combine(targetRoot, MarkerFileName);
        if (File.Exists(markerPath))
        {
            if (TryReadValidMarker(markerPath, out _))
            {
                return;
            }

            if (!allowUnmarked)
            {
                throw new PublishTargetException(
                    $"目标目录 {targetRoot} 的 {MarkerFileName} 无法识别为 Bocchi 发布标记。"
                    + "确认这是你要覆盖的目录后，在方案里勾选「允许覆盖无标记目录」，或先清空该目录。");
            }

            return;
        }

        var hasAnyFile = Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories).Any();
        if (!hasAnyFile)
        {
            return;
        }

        if (!allowUnmarked)
        {
            throw new PublishTargetException(
                $"目标目录 {targetRoot} 里已有文件，且没有合法的 {MarkerFileName}。"
                + "确认这是你要覆盖的目录后，在方案里勾选「允许覆盖无标记目录」，或先清空该目录。");
        }
    }

    /// <summary>读取并校验标记 JSON；供测试与发布共用。</summary>
    internal static bool TryReadValidMarker(string markerPath, out LocalPublishMarker? marker)
    {
        marker = null;
        try
        {
            var json = File.ReadAllText(markerPath);
            var parsed = JsonSerializer.Deserialize<LocalPublishMarker>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            // schemaVersion 缺省（旧标记为 0）视为 1，其它字段仍须齐全
            if (parsed.SchemaVersion < 0)
            {
                return false;
            }

            if (parsed.FileCount < 0)
            {
                return false;
            }

            if (parsed.Files is null)
            {
                return false;
            }

            if (parsed.GeneratedAtUtc == default)
            {
                return false;
            }

            marker = parsed with { SchemaVersion = parsed.SchemaVersion <= 0 ? 1 : parsed.SchemaVersion };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void PruneEmptyDirectories(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir, recursive: false);
            }
        }
    }

    private static bool IsFilesystemRoot(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            || full is "/" or "\\";
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string candidate, string parent)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullParent = Path.GetFullPath(parent);
        var parentWithSep = fullParent.EndsWith(Path.DirectorySeparatorChar)
            ? fullParent
            : fullParent + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(parentWithSep, StringComparison.OrdinalIgnoreCase)
            || PathsEqual(fullCandidate, fullParent);
    }

    /// <summary>文件路径必须位于 targetRoot 之下（文件本身不能等于 root）。</summary>
    private static bool IsStrictlyUnderOrEqualFile(string destinationFile, string targetRoot)
    {
        var fullFile = Path.GetFullPath(destinationFile);
        var fullRoot = Path.GetFullPath(targetRoot);
        if (PathsEqual(fullFile, fullRoot))
        {
            return false;
        }

        var rootWithSep = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullFile.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record LocalPublishMarker(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        int FileCount,
        string? BuildFingerprint,
        IReadOnlyList<string> Files);
}
