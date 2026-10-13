using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Exceptions;
using Bocchi.Generator.Theme;
using Bocchi.Generator.Utilities;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 计算本次构建的全局指纹。在 <see cref="WriteThemeInputStage"/> 之后运行：对已落盘的
/// theme-input 目录做稳定全量 hash，再叠加主题源文件、媒体 sha 与 BocchiVersion。
/// 这样内容字段漏列不会再让「改了标题/标签/摘要/封面/导航却指纹不变」复发。
/// </summary>
public sealed class ComputeFingerprintStage : IBuildStage
{
    private readonly BocchiDataLayout _layout;

    public ComputeFingerprintStage(BocchiDataLayout layout)
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
        AppendLine(sha, "bocchi-fingerprint:v2");
        AppendLine(sha, $"mode={session.Options.Mode}");
        AppendLine(sha, $"env={session.Options.Environment}");
        AppendLine(sha, $"includeDrafts={session.Options.IncludeDrafts}");
        // Feed 在短路之后写出，必须单独入指纹，否则只改 FeedItemCount 会被错误跳过。
        AppendLine(sha, $"feedItemCount={session.Options.FeedItemCount ?? session.Graph.Site.Settings.FeedItemCount}");
        AppendLine(sha, $"bocchiVersion={session.GetItem<string>(BuildSessionKeys.BocchiVersion) ?? string.Empty}");

        var themeInputRoot = ResolveThemeInputRoot(session);
        AppendThemeInputTree(sha, themeInputRoot);
        AppendThemeFileHashes(sha, session.GetItem<LoadedTheme>(BuildSessionKeys.LoadedTheme));

        foreach (var asset in session.Graph.MediaAssets.OrderBy(a => a.SiteRelativePath, StringComparer.Ordinal))
        {
            AppendLine(sha, $"media:{asset.SiteRelativePath}|sha={asset.Sha256}|size={asset.SizeBytes}");
        }

        session.Fingerprint = new BuildFingerprint(Sha256Hex.ToLowerHex(sha.GetCurrentHash()));
        session.Log(Name, BuildLogLevel.Info, $"指纹 = {session.Fingerprint.Value}");
        return Task.FromResult(true);
    }

    /// <summary>Full Build 用 DataRoot 下的 theme-input；Live 预览用临时目录。</summary>
    private string ResolveThemeInputRoot(BuildSession session)
    {
        if (session.Options.Mode == BuildMode.Live &&
            !string.IsNullOrWhiteSpace(session.Options.LiveThemeInputDirectory))
        {
            return Path.GetFullPath(session.Options.LiveThemeInputDirectory);
        }

        return Path.GetFullPath(_layout.ThemeInputDirectory);
    }

    /// <summary>按相对路径排序后把目录内每个普通文件的路径与内容 hash 写入指纹。</summary>
    private static void AppendThemeInputTree(IncrementalHash sha, string themeInputRoot)
    {
        if (!Directory.Exists(themeInputRoot))
        {
            AppendLine(sha, "themeInput=missing");
            return;
        }

        var root = Path.GetFullPath(themeInputRoot);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => IsRegularFileUnder(root, path))
            .OrderBy(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            AppendLine(sha, "themeInput=empty");
            return;
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var bytes = File.ReadAllBytes(file);
            // 用稳定内容 hash；不要用原始 size（generatedAt 会让字节长度每次不同）。
            var contentHash = HashThemeInputFile(relative, bytes);
            AppendLine(sha, $"themeInput:{relative}|sha={contentHash}");
        }
    }

    /// <summary>
    /// JSON 输入里的 generatedAt / buildTime 每次构建都会变，不能进指纹；
    /// 其它文件按原始字节 hash。
    /// </summary>
    private static string HashThemeInputFile(string relativePath, byte[] bytes)
    {
        if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Sha256Hex.FromBytes(bytes);
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteJsonOmittingVolatile(doc.RootElement, writer);
            }

            return Sha256Hex.FromBytes(stream.ToArray());
        }
        catch (JsonException)
        {
            return Sha256Hex.FromBytes(bytes);
        }
    }

    private static readonly HashSet<string> VolatileJsonProperties = new(StringComparer.Ordinal)
    {
        "generatedAt",
        "buildTime",
    };

    private static void WriteJsonOmittingVolatile(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (VolatileJsonProperties.Contains(property.Name))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteJsonOmittingVolatile(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteJsonOmittingVolatile(item, writer);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsRegularFileUnder(string root, string filePath)
    {
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            return false;
        }

        var attrs = File.GetAttributes(filePath);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        var relative = Path.GetRelativePath(root, filePath).Replace(Path.DirectorySeparatorChar, '/');
        return !relative.Split('/').Any(segment =>
            string.Equals(segment, ".git", StringComparison.Ordinal) ||
            string.Equals(segment, "node_modules", StringComparison.Ordinal));
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
            var relativePath = Path.GetRelativePath(themeRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            AppendLine(sha, $"themeFile:{relativePath}|sha={Sha256Hex.FromBytes(bytes)}|size={bytes.Length}");
        }
    }

    private static bool IsThemeSourceFile(string themeRoot, string outputRoot, string filePath)
    {
        if (!IsUnderDirectory(filePath, themeRoot) || IsUnderDirectory(filePath, outputRoot))
        {
            return false;
        }

        var attrs = File.GetAttributes(filePath);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        var relativePath = Path.GetRelativePath(themeRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
        return !relativePath.Split('/').Any(segment =>
            string.Equals(segment, ".git", StringComparison.Ordinal) ||
            string.Equals(segment, "node_modules", StringComparison.Ordinal));
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedDirectory, StringComparison.Ordinal);
    }

    private static void AppendLine(IncrementalHash sha, string line)
    {
        sha.AppendData(Encoding.UTF8.GetBytes(line));
        sha.AppendData([0x0a]);
    }
}
