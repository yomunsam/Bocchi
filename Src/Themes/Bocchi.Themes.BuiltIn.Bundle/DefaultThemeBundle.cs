using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bocchi.Themes.BuiltIn.Bundle;

/// <summary>系统默认 Theme 的身份信息与运行实例物化入口。</summary>
public static class DefaultThemeBundle
{
    /// <summary>默认 Theme id。该值同时出现在 site.yaml、theme.json 和 Dashboard 配置中。</summary>
    public const string ThemeId = "bocchi-mono";

    /// <summary>默认 Theme 显示名称。</summary>
    public const string ThemeName = "Bocchi Mono";

    /// <summary>默认 Theme 版本；内置文件有变更时递增。</summary>
    public const string ThemeVersion = "0.3.0";

    /// <summary>存放「上次从内置包写入的文件哈希」的目录名，挂在 themes/ 下。</summary>
    public const string BundleHashesDirectoryName = ".bundle-hashes";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// 把随包分发的默认 Theme 物化到 DataRoot themes 目录。
    /// 缺失文件会补上；内容仍等于上次（或当前）内置哈希的文件会被升级/跟踪；用户改过的文件一律保留并出现在返回列表里（含首次建档）。
    /// </summary>
    public static async Task<IReadOnlyList<string>> EnsureAsync(string themesDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themesDirectory);

        var themeRoot = Path.Combine(themesDirectory, ThemeId);
        Directory.CreateDirectory(themeRoot);

        var hashPath = ResolveHashManifestPath(themesDirectory);
        var previous = await ReadHashManifestAsync(hashPath, cancellationToken).ConfigureAwait(false);
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        var edited = new List<string>();

        foreach (var relativePath in DefaultThemeBundleResources.Files)
        {
            var destination = Path.Combine(themeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            await using var bundled = DefaultThemeBundleResources.OpenRequired(relativePath);
            var bundledHash = await HashAsync(bundled, cancellationToken).ConfigureAwait(false);
            bundled.Position = 0;

            if (!File.Exists(destination))
            {
                await DefaultThemeBundleResources.CopyStreamToFileAsync(bundled, destination, overwrite: false, cancellationToken)
                    .ConfigureAwait(false);
                next[relativePath] = bundledHash;
                continue;
            }

            var installedHash = await HashFileAsync(destination, cancellationToken).ConfigureAwait(false);
            previous.TryGetValue(relativePath, out var lastBundledHash);

            // 尚无该文件的哈希记录（含首次建档）：与升级相同——已等于当前内置则跟踪；否则保留用户文件并标为已编辑
            if (lastBundledHash is null)
            {
                if (string.Equals(installedHash, bundledHash, StringComparison.Ordinal))
                {
                    next[relativePath] = bundledHash;
                    continue;
                }

                edited.Add(relativePath);
                // 以当前内置哈希为基线，后续升级仍能识别「用户相对该版本已改过」
                next[relativePath] = bundledHash;
                continue;
            }

            // 用户没改过：磁盘内容仍等于上次写入的内置哈希
            if (string.Equals(installedHash, lastBundledHash, StringComparison.Ordinal))
            {
                if (!string.Equals(installedHash, bundledHash, StringComparison.Ordinal))
                {
                    await DefaultThemeBundleResources.CopyStreamToFileAsync(bundled, destination, overwrite: true, cancellationToken)
                        .ConfigureAwait(false);
                }

                next[relativePath] = bundledHash;
                continue;
            }

            // 用户改过：保留本地文件，哈希清单仍记上次内置值，避免下次误当成「未改」
            edited.Add(relativePath);
            next[relativePath] = lastBundledHash;
        }

        await WriteHashManifestAsync(hashPath, next, cancellationToken).ConfigureAwait(false);
        return edited;
    }

    /// <summary>哈希清单路径：&lt;themes&gt;/.bundle-hashes/bocchi-mono.json。</summary>
    public static string ResolveHashManifestPath(string themesDirectory)
        => Path.Combine(themesDirectory, BundleHashesDirectoryName, ThemeId + ".json");

    private static async Task<Dictionary<string, string>> ReadHashManifestAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await using var stream = File.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return data is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(data, StringComparer.Ordinal);
    }

    private static async Task WriteHashManifestAsync(
        string path,
        IReadOnlyDictionary<string, string> hashes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        await JsonSerializer.SerializeAsync(stream, hashes, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        return await HashAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> HashAsync(Stream stream, CancellationToken cancellationToken)
    {
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
