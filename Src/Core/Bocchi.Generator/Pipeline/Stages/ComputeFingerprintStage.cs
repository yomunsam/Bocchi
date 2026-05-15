using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Theme;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 计算本次构建的全局指纹：把内容源、媒体、站点设置、Theme 配置、Theme 源文件和构建选项扁平化为稳定字节串，
/// 再 SHA-256 取小写十六进制。详见 <c>Docs/Milestones/M3/M3.md §3.7</c> 与 M5 Theme 输出链路。
/// </summary>
public sealed class ComputeFingerprintStage : IBuildStage
{
    private readonly WorkspaceLayout _layout;

    public ComputeFingerprintStage(WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    public string Name => nameof(ComputeFingerprintStage);

    public Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Graph is null)
        {
            throw new BuildPipelineException($"{Name} 需要内容图。");
        }

        var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendLine(sha, $"bocchi-fingerprint:v1");
        AppendLine(sha, $"mode={session.Options.Mode}");
        AppendLine(sha, $"env={session.Options.Environment}");
        AppendLine(sha, $"includeDrafts={session.Options.IncludeDrafts}");
        AppendLine(sha, $"feedItemCount={session.Options.FeedItemCount ?? session.Graph.Site.Settings.FeedItemCount}");
        AppendLine(sha, $"baseUrl={session.Graph.Site.NormalizedBaseUrl}");
        var themeId = session.GetItem<string>(BuildSessionKeys.ThemeId) ?? session.Graph.Site.Settings.DefaultThemeId ?? string.Empty;
        AppendLine(sha, $"themeId={themeId}");
        AppendThemeConfigHash(sha, _layout.ThemeConfigDirectory, themeId);
        AppendThemeFileHashes(sha, session.GetItem<LoadedTheme>(BuildSessionKeys.LoadedTheme));
        AppendLine(sha, $"bocchiVersion={session.GetItem<string>(BuildSessionKeys.BocchiVersion) ?? string.Empty}");

        // 站点设置 / 导航：用全字段 toString 模拟稳定快照
        AppendLine(sha, $"site={session.Graph.Site.Settings}");

        // 内容：每篇按 site-relative-url 排序后 hash 关键字段
        foreach (var post in session.Graph.Posts.OrderBy(p => p.SiteRelativeUrl, StringComparer.Ordinal))
        {
            AppendLine(sha, $"post:{post.SiteRelativeUrl}|st={post.Status}|pub={post.PublishedAt?.UtcTicks}|up={post.UpdatedAt?.UtcTicks}");
            AppendBodyHash(sha, post.BodyMarkdown);
        }

        foreach (var page in session.Graph.Pages.OrderBy(p => p.SiteRelativeUrl, StringComparer.Ordinal))
        {
            AppendLine(sha, $"page:{page.SiteRelativeUrl}|st={page.Status}");
            AppendBodyHash(sha, page.BodyMarkdown);
        }

        foreach (var work in session.Graph.Works.OrderBy(p => p.SiteRelativeUrl, StringComparer.Ordinal))
        {
            AppendLine(sha, $"work:{work.SiteRelativeUrl}|st={work.Status}|featured={work.Featured}");
            AppendBodyHash(sha, work.BodyMarkdown);
        }

        foreach (var note in session.Graph.Notes.OrderBy(p => p.Year + "/" + p.Id, StringComparer.Ordinal))
        {
            AppendLine(sha, $"note:{note.Year}/{note.Id}|st={note.Status}|pub={note.PublishedAt?.UtcTicks}");
            AppendBodyHash(sha, note.BodyMarkdown);
        }

        foreach (var friend in session.Graph.Friends.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            AppendLine(sha, $"friend:{friend.Name}|{friend.Url}|st={friend.Status}|order={friend.Order}");
        }

        foreach (var asset in session.Graph.MediaAssets.OrderBy(a => a.SiteRelativePath, StringComparer.Ordinal))
        {
            AppendLine(sha, $"media:{asset.SiteRelativePath}|sha={asset.Sha256}|size={asset.SizeBytes}");
        }

        var digest = sha.GetCurrentHash();
        var sb = new StringBuilder(digest.Length * 2);
        foreach (var b in digest)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        session.Fingerprint = new BuildFingerprint(sb.ToString());
        session.Log(Name, BuildLogLevel.Info, $"指纹 = {session.Fingerprint.Value}");
        return Task.FromResult(true);
    }

    private static void AppendLine(IncrementalHash sha, string line)
    {
        sha.AppendData(Encoding.UTF8.GetBytes(line));
        sha.AppendData([0x0a]);
    }

    private static void AppendBodyHash(IncrementalHash sha, string body)
    {
        Span<byte> bodyHash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(body), bodyHash);
        sha.AppendData(bodyHash);
    }

    private static void AppendThemeConfigHash(IncrementalHash sha, string themeConfigDirectory, string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId) ||
            themeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            themeId.Contains('/') ||
            themeId.Contains('\\') ||
            string.Equals(themeId, ".", StringComparison.Ordinal) ||
            string.Equals(themeId, "..", StringComparison.Ordinal))
        {
            AppendLine(sha, "themeConfig=none");
            return;
        }

        var path = Path.Combine(themeConfigDirectory, themeId + ".json");
        if (!File.Exists(path))
        {
            AppendLine(sha, "themeConfig=missing");
            return;
        }

        Span<byte> fileHash = stackalloc byte[32];
        SHA256.HashData(File.ReadAllBytes(path), fileHash);
        var sb = new StringBuilder(fileHash.Length * 2);
        foreach (var b in fileHash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        AppendLine(sha, $"themeConfig={sb}");
    }

    /// <summary>把 Theme 源文件纳入构建指纹，避免模板或 CSS 修改后被错误短路。</summary>
    private static void AppendThemeFileHashes(IncrementalHash sha, LoadedTheme? loadedTheme)
    {
        if (loadedTheme is null || !Directory.Exists(loadedTheme.ThemeRoot))
        {
            AppendLine(sha, "themeFiles=missing");
            return;
        }

        var themeRoot = Path.GetFullPath(loadedTheme.ThemeRoot);
        var outputRoot = Path.GetFullPath(Path.Combine(themeRoot, loadedTheme.Manifest.OutputDir));
        foreach (var file in Directory.EnumerateFiles(themeRoot, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => IsThemeSourceFile(themeRoot, outputRoot, path))
            .OrderBy(path => Path.GetRelativePath(themeRoot, path), StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(file);
            var fileHash = SHA256.HashData(bytes);
            var relativePath = Path.GetRelativePath(themeRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            AppendLine(sha, $"themeFile:{relativePath}|sha={ToHex(fileHash)}|size={bytes.Length}");
        }
    }

    /// <summary>判断某个 Theme 文件是否属于用户可编辑源，而不是构建输出或依赖缓存。</summary>
    private static bool IsThemeSourceFile(string themeRoot, string outputRoot, string filePath)
    {
        if (!IsUnderDirectory(filePath, themeRoot) || IsUnderDirectory(filePath, outputRoot))
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(themeRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
        return !relativePath.Split('/').Any(segment =>
            string.Equals(segment, ".git", StringComparison.Ordinal) ||
            string.Equals(segment, "node_modules", StringComparison.Ordinal));
    }

    /// <summary>判断文件是否在指定目录下，目录本身不算作命中。</summary>
    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedDirectory, StringComparison.Ordinal);
    }

    /// <summary>把 hash bytes 转为小写十六进制字符串。</summary>
    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
