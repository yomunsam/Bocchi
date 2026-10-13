using System.Text;
using System.Text.Json;

using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>把 <c>output/public/</c> 镜像同步到本机某个绝对目录。</summary>
public sealed class LocalDirectoryPublisher : IPublishTargetPublisher
{
    /// <summary>目标目录里的归属标记；有它才允许自动删掉多余文件。</summary>
    public const string MarkerFileName = ".bocchi-local-publish.json";

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
    public Task<PublishTargetResult> PublishAsync(PublishTargetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = LocalDirectoryPublishConfiguration.FromJson(request.ConfigurationJson).Normalize();
        var targetRoot = ValidateAndResolveTarget(configuration);

        Directory.CreateDirectory(targetRoot);
        EnsureTargetIsSafeToMirror(targetRoot, configuration.AllowUnmarkedTarget);

        var sourceFiles = request.Output.Files;
        var relativeSet = new HashSet<string>(sourceFiles.Select(f => f.RelativePath), StringComparer.Ordinal);

        foreach (var file in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(targetRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file.AbsolutePath, destination, overwrite: true);
        }

        // 删掉目标里多出来的文件（含旧标记，随后重写）
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
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            FileCount: sourceFiles.Count,
            BuildFingerprint: request.BuildResult?.Fingerprint?.Value,
            Files: relativeSet.Order(StringComparer.Ordinal).ToArray());
        var markerJson = JsonSerializer.Serialize(marker, JsonOptions);
        File.WriteAllText(Path.Combine(targetRoot, MarkerFileName), markerJson + "\n", Encoding.UTF8);

        return Task.FromResult(new PublishTargetResult
        {
            RemoteCommitSha = null,
            RemoteUrl = new Uri(targetRoot).AbsoluteUri,
        });
    }

    /// <summary>路径必须是绝对路径，且不能指向 Bocchi DataRoot 里的关键目录。</summary>
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

        if (IsFilesystemRoot(target))
        {
            throw new PublishTargetException("不能把站点发布到文件系统根目录。");
        }

        var dataRoot = Path.GetFullPath(_layout.DataRoot);
        if (PathsEqual(target, dataRoot)
            || IsUnder(target, _layout.WorkspaceRoot)
            || IsUnder(target, _layout.StateDirectory)
            || IsUnder(target, _layout.ThemesDirectory)
            || IsUnder(target, _layout.DataProtectionKeysDirectory))
        {
            throw new PublishTargetException(
                "本地发布目录不能是 Bocchi 的 DataRoot、workspace、state、themes 或 keys。请另选一个目录。");
        }

        return target;
    }

    /// <summary>非空且无标记的目标目录默认拒绝，防止误删。</summary>
    private static void EnsureTargetIsSafeToMirror(string targetRoot, bool allowUnmarked)
    {
        var markerPath = Path.Combine(targetRoot, MarkerFileName);
        if (File.Exists(markerPath))
        {
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
                $"目标目录 {targetRoot} 里已有文件，且没有 {MarkerFileName}。"
                + "确认这是你要覆盖的目录后，在方案里勾选「允许覆盖无标记目录」，或先清空该目录。");
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

    private sealed record LocalPublishMarker(
        DateTimeOffset GeneratedAtUtc,
        int FileCount,
        string? BuildFingerprint,
        IReadOnlyList<string> Files);
}
