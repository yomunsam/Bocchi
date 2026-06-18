using System.Text.Json;

using Bocchi.GeneratorContract;
using Bocchi.Themes.BuiltIn.Bundle;
using Bocchi.Workspace;

using Microsoft.Extensions.Options;

namespace Bocchi.Generator.Theme;

/// <summary>
/// 统一解析 Theme 来源的服务。Generator、Home Server 和后续 Theme Library 都应通过它取得 Theme Root。
/// </summary>
public sealed class ThemeResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly BocchiDataLayout _layout;
    private readonly IOptions<ThemeDevelopmentOptions> _options;

    /// <summary>构造 Theme Resolver。</summary>
    public ThemeResolver(BocchiDataLayout layout, IOptions<ThemeDevelopmentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        _layout = layout;
        _options = options;
    }

    /// <summary>列出当前 Catalog 中可见的 Theme；无效 Theme 也保留为带诊断的条目。</summary>
    public async Task<IReadOnlyList<ThemeCatalogItem>> ListAvailableThemesAsync(CancellationToken cancellationToken = default)
    {
        await DefaultThemeBundle.EnsureAsync(_layout.ThemesDirectory, cancellationToken).ConfigureAwait(false);

        var items = new Dictionary<string, ThemeCatalogItem>(StringComparer.Ordinal);
        foreach (var themeDirectory in Directory.EnumerateDirectories(_layout.ThemesDirectory).Order(StringComparer.Ordinal))
        {
            var themeId = Path.GetFileName(themeDirectory);
            if (ShouldIgnoreInstalledDirectory(themeId))
            {
                continue;
            }

            var sourceKind = string.Equals(themeId, DefaultThemeBundle.ThemeId, StringComparison.Ordinal)
                ? ThemeSourceKind.BuiltIn
                : ThemeSourceKind.Installed;
            var item = await InspectThemeRootAsync(themeId, themeDirectory, sourceKind, shadowsInstalledTheme: false, [], cancellationToken)
                .ConfigureAwait(false);
            items[item.Id] = item;
        }

        if (_options.Value.AreDevLinksEnabled)
        {
            var devLinks = await ReadDevLinksAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in await InspectDevLinksAsync(devLinks.Links, items.Keys, cancellationToken).ConfigureAwait(false))
            {
                items[item.Id] = item;
            }
        }

        return items.Values
            .OrderByDescending(x => string.Equals(x.Id, DefaultThemeBundle.ThemeId, StringComparison.Ordinal))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>按 Theme id 解析单个 Theme；Dev Link 启用且同 id 存在时优先于 Installed Theme。</summary>
    public async Task<ThemeResolveResult> ResolveThemeAsync(string themeId, CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = NormalizeThemeId(themeId);
        if (!IsValidThemeId(normalizedThemeId))
        {
            return Unresolved(normalizedThemeId, Error("theme-id-invalid", $"Theme id '{themeId}' 不是合法的 Theme 目录名。"));
        }

        if (_options.Value.AreDevLinksEnabled)
        {
            var devLinks = await ReadDevLinksAsync(cancellationToken).ConfigureAwait(false);
            var matchingDevLinks = devLinks.Links
                .Where(x => x.Enabled && string.Equals(NormalizeThemeId(x.Id), normalizedThemeId, StringComparison.Ordinal))
                .ToList();
            if (matchingDevLinks.Count > 0)
            {
                var duplicateDiagnostic = matchingDevLinks.Count > 1
                    ? Error("dev-link-duplicate-id", $"Dev Link 清单中存在重复的 Theme id '{normalizedThemeId}'。")
                    : null;
                var item = await InspectDevLinkAsync(
                        matchingDevLinks[0],
                        duplicateDiagnostic is null ? [] : [duplicateDiagnostic],
                        shadowsInstalledTheme: InstalledThemeRootExists(normalizedThemeId),
                        cancellationToken)
                    .ConfigureAwait(false);
                return ToResolveResult(normalizedThemeId, item);
            }
        }

        if (string.Equals(normalizedThemeId, DefaultThemeBundle.ThemeId, StringComparison.Ordinal))
        {
            await DefaultThemeBundle.EnsureAsync(_layout.ThemesDirectory, cancellationToken).ConfigureAwait(false);
        }

        var installedRoot = Path.Combine(_layout.ThemesDirectory, normalizedThemeId);
        var installedSource = string.Equals(normalizedThemeId, DefaultThemeBundle.ThemeId, StringComparison.Ordinal)
            ? ThemeSourceKind.BuiltIn
            : ThemeSourceKind.Installed;
        var installedItem = await InspectThemeRootAsync(
                normalizedThemeId,
                installedRoot,
                installedSource,
                shadowsInstalledTheme: false,
                [],
                cancellationToken)
            .ConfigureAwait(false);
        return ToResolveResult(normalizedThemeId, installedItem);
    }

    /// <summary>判断 Theme id 是否可安全映射到 DataRoot 下的单个目录名。</summary>
    public static bool IsValidThemeId(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId) ||
            !string.Equals(themeId, themeId.Trim(), StringComparison.Ordinal) ||
            string.Equals(themeId, ".", StringComparison.Ordinal) ||
            string.Equals(themeId, "..", StringComparison.Ordinal) ||
            themeId.Contains('/') ||
            themeId.Contains('\\') ||
            themeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return themeId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');
    }

    private static async Task<IReadOnlyList<ThemeCatalogItem>> InspectDevLinksAsync(
        IReadOnlyList<ThemeDevLinkEntry> links,
        IEnumerable<string> existingThemeIds,
        CancellationToken cancellationToken)
    {
        var enabledLinks = links.Where(x => x.Enabled).ToList();
        var duplicateIds = enabledLinks
            .Select(x => NormalizeThemeId(x.Id))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.Ordinal);
        var installedThemeIds = existingThemeIds.ToHashSet(StringComparer.Ordinal);
        var result = new List<ThemeCatalogItem>();

        foreach (var link in enabledLinks)
        {
            var id = NormalizeThemeId(link.Id);
            var diagnostics = duplicateIds.Contains(id)
                ? new[] { Error("dev-link-duplicate-id", $"Dev Link 清单中存在重复的 Theme id '{id}'。") }
                : [];
            var item = await InspectDevLinkAsync(
                    link,
                    diagnostics,
                    shadowsInstalledTheme: installedThemeIds.Contains(id),
                    cancellationToken)
                .ConfigureAwait(false);
            result.Add(item);
        }

        return result;
    }

    private static async Task<ThemeCatalogItem> InspectDevLinkAsync(
        ThemeDevLinkEntry link,
        IEnumerable<ThemeDiagnostic> diagnostics,
        bool shadowsInstalledTheme,
        CancellationToken cancellationToken)
    {
        var themeId = NormalizeThemeId(link.Id);
        var root = string.IsNullOrWhiteSpace(link.Root) ? link.Root : Path.GetFullPath(link.Root);
        var linkDiagnostics = diagnostics.ToList();
        if (string.IsNullOrWhiteSpace(link.Root) || !Path.IsPathFullyQualified(link.Root))
        {
            linkDiagnostics.Add(Error("dev-link-root-not-absolute", $"Dev Link '{themeId}' 的 root 必须是绝对路径。"));
        }

        return await InspectThemeRootAsync(
                themeId,
                root,
                ThemeSourceKind.DevLink,
                shadowsInstalledTheme,
                linkDiagnostics,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ThemeCatalogItem> InspectThemeRootAsync(
        string expectedThemeId,
        string themeRoot,
        ThemeSourceKind sourceKind,
        bool shadowsInstalledTheme,
        IEnumerable<ThemeDiagnostic> leadingDiagnostics,
        CancellationToken cancellationToken)
    {
        var diagnostics = leadingDiagnostics.ToList();
        var normalizedThemeId = NormalizeThemeId(expectedThemeId);
        var normalizedRoot = string.IsNullOrWhiteSpace(themeRoot) ? themeRoot : Path.GetFullPath(themeRoot);
        ThemeManifest? manifest = null;

        if (!IsValidThemeId(normalizedThemeId))
        {
            diagnostics.Add(Error("theme-id-invalid", $"Theme id '{expectedThemeId}' 不是合法的 Theme 目录名。"));
        }

        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            diagnostics.Add(Error("theme-root-missing", $"Theme Root '{themeRoot}' 不存在。"));
        }
        else
        {
            try
            {
                var loaded = await ThemeManifestLoader.TryLoadFromRootAsync(normalizedRoot, cancellationToken).ConfigureAwait(false);
                if (loaded is null)
                {
                    diagnostics.Add(Error("theme-manifest-missing", $"Theme Root '{normalizedRoot}' 缺少 theme.json。"));
                }
                else
                {
                    manifest = loaded.Value.Manifest;
                    ValidateManifest(normalizedThemeId, manifest, normalizedRoot, diagnostics);
                }
            }
            catch (JsonException ex)
            {
                diagnostics.Add(Error("theme-manifest-json-invalid", $"Theme Root '{normalizedRoot}' 的 theme.json 无法解析：{ex.Message}"));
            }
            catch (IOException ex)
            {
                diagnostics.Add(Error("theme-manifest-read-failed", $"Theme Root '{normalizedRoot}' 的 theme.json 读取失败：{ex.Message}"));
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics.Add(Error("theme-manifest-read-denied", $"Theme Root '{normalizedRoot}' 的 theme.json 无法访问：{ex.Message}"));
            }
        }

        if (shadowsInstalledTheme)
        {
            diagnostics.Add(new ThemeDiagnostic(
                ThemeDiagnosticSeverity.Info,
                "dev-link-shadows-installed",
                $"Dev Link Theme '{normalizedThemeId}' 正在覆盖同 id 的 Installed Theme。"));
        }

        return new ThemeCatalogItem
        {
            Id = normalizedThemeId,
            Name = string.IsNullOrWhiteSpace(manifest?.Name) ? normalizedThemeId : manifest.Name.Trim(),
            Version = string.IsNullOrWhiteSpace(manifest?.Version) ? null : manifest.Version.Trim(),
            ContractVersion = string.IsNullOrWhiteSpace(manifest?.ContractVersion) ? null : manifest.ContractVersion.Trim(),
            Root = normalizedRoot,
            SourceKind = sourceKind,
            Manifest = manifest,
            RunnerKind = ResolveRunnerKind(manifest),
            Diagnostics = diagnostics,
            ShadowsInstalledTheme = shadowsInstalledTheme,
        };
    }

    private async Task<DevLinksReadResult> ReadDevLinksAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_layout.ThemesDirectory, "dev-links.json");
        if (!File.Exists(path))
        {
            return new DevLinksReadResult([], []);
        }

        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            var manifest = await JsonSerializer.DeserializeAsync<ThemeDevLinksManifest>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return new DevLinksReadResult(manifest?.Links ?? [], []);
        }
        catch (JsonException ex)
        {
            return new DevLinksReadResult([], [Error("dev-links-json-invalid", $"dev-links.json 无法解析：{ex.Message}")]);
        }
        catch (IOException ex)
        {
            return new DevLinksReadResult([], [Error("dev-links-read-failed", $"dev-links.json 读取失败：{ex.Message}")]);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new DevLinksReadResult([], [Error("dev-links-read-denied", $"dev-links.json 无法访问：{ex.Message}")]);
        }
    }

    private static void ValidateManifest(string expectedThemeId, ThemeManifest manifest, string themeRoot, List<ThemeDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || !IsValidThemeId(manifest.Id.Trim()))
        {
            diagnostics.Add(Error("theme-manifest-id-invalid", "theme.json.id 必须是合法的 Theme id。"));
        }
        else if (!string.Equals(manifest.Id.Trim(), expectedThemeId, StringComparison.Ordinal))
        {
            diagnostics.Add(Error("theme-manifest-id-mismatch", $"theme.json.id '{manifest.Id}' 与期望 Theme id '{expectedThemeId}' 不一致。"));
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            diagnostics.Add(Error("theme-manifest-name-empty", "theme.json.name 不能为空。"));
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            diagnostics.Add(Error("theme-manifest-version-empty", "theme.json.version 不能为空。"));
        }

        if (!string.Equals(manifest.ContractVersion, ThemeContractVersion.Current, StringComparison.Ordinal))
        {
            diagnostics.Add(Error("theme-contract-unsupported", $"Theme Contract '{manifest.ContractVersion}' 不受当前 Bocchi 支持。"));
        }

        ValidateRunner(manifest, diagnostics);
        diagnostics.AddRange(ThemeManifestValidator.ValidatePrivateI18nNamespace(manifest));
        diagnostics.AddRange(ThemeStaticAssetManifestValidator.Validate(manifest, themeRoot));
    }

    private static void ValidateRunner(ThemeManifest manifest, List<ThemeDiagnostic> diagnostics)
    {
        if (manifest.Runner is null)
        {
            if (manifest.Build is null || string.IsNullOrWhiteSpace(manifest.Build.Command))
            {
                diagnostics.Add(Error("theme-runner-missing", "theme.json 必须声明 runner 或旧版 build.command。"));
            }

            return;
        }

        var kind = manifest.Runner.Kind.Trim();
        if (string.Equals(kind, "fluid-static", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(kind, "process", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Error("theme-runner-unsupported", $"runner.kind '{manifest.Runner.Kind}' 尚未由当前 Generator 支持。"));
            return;
        }

        var command = string.IsNullOrWhiteSpace(manifest.Runner.Command)
            ? manifest.Build?.Command
            : manifest.Runner.Command;
        if (string.IsNullOrWhiteSpace(command))
        {
            diagnostics.Add(Error("theme-process-command-missing", "process runner 必须声明 command。"));
        }
    }

    private static string? ResolveRunnerKind(ThemeManifest? manifest)
    {
        if (manifest is null)
        {
            return null;
        }

        if (manifest.Runner is null)
        {
            return string.IsNullOrWhiteSpace(manifest.Build?.Command) ? null : "process";
        }

        return manifest.Runner.Kind.Trim();
    }

    private bool InstalledThemeRootExists(string themeId)
        => Directory.Exists(Path.Combine(_layout.ThemesDirectory, themeId));

    private static ThemeResolveResult ToResolveResult(string themeId, ThemeCatalogItem item)
    {
        if (!item.IsAvailable || item.Manifest is null)
        {
            return Unresolved(themeId, item.Diagnostics);
        }

        return new ThemeResolveResult
        {
            Id = themeId,
            Theme = new ResolvedTheme
            {
                Id = item.Manifest.Id.Trim(),
                Name = item.Manifest.Name.Trim(),
                Version = item.Manifest.Version.Trim(),
                ContractVersion = item.Manifest.ContractVersion.Trim(),
                Root = item.Root,
                SourceKind = item.SourceKind,
                Manifest = item.Manifest,
                Diagnostics = item.Diagnostics,
                ShadowsInstalledTheme = item.ShadowsInstalledTheme,
            },
            Diagnostics = item.Diagnostics,
        };
    }

    private static ThemeResolveResult Unresolved(string themeId, params ThemeDiagnostic[] diagnostics)
        => Unresolved(themeId, (IReadOnlyList<ThemeDiagnostic>)diagnostics);

    private static ThemeResolveResult Unresolved(string themeId, IReadOnlyList<ThemeDiagnostic> diagnostics)
        => new()
        {
            Id = themeId,
            Diagnostics = diagnostics,
        };

    private static string NormalizeThemeId(string themeId)
        => string.IsNullOrWhiteSpace(themeId) ? string.Empty : themeId.Trim();

    private static bool ShouldIgnoreInstalledDirectory(string themeId)
        => string.IsNullOrWhiteSpace(themeId) ||
           string.Equals(themeId, ".backups", StringComparison.Ordinal);

    private static ThemeDiagnostic Error(string code, string message)
        => new(ThemeDiagnosticSeverity.Error, code, message);

    /// <summary>Dev Link 清单读取结果；清单级诊断预留给后续诊断页展示。</summary>
    private sealed record DevLinksReadResult(
        IReadOnlyList<ThemeDevLinkEntry> Links,
        IReadOnlyList<ThemeDiagnostic> Diagnostics);
}
