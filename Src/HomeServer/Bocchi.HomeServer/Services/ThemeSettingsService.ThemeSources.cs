using Bocchi.Generator.Theme;
using Bocchi.GeneratorContract;
using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>Theme catalog、Theme resolver 和配置记录持久化辅助。</summary>
public sealed partial class ThemeSettingsService
{
    /// <summary>列出当前可选择的前台 Theme；内置默认 Theme 始终会被物化并排在第一位。</summary>
    public async Task<IReadOnlyList<ThemeOption>> ListAvailableThemesAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await ListThemeCatalogAsync(cancellationToken).ConfigureAwait(false);
        return catalog
            .Where(x => x.IsAvailable)
            .Select(x => new ThemeOption(x.Id, x.Name)
            {
                Version = x.Version,
                ContractVersion = x.ContractVersion,
                Root = x.Root,
                SourceKind = x.SourceKind,
                RunnerKind = x.RunnerKind,
                Diagnostics = x.Diagnostics,
                ShadowsInstalledTheme = x.ShadowsInstalledTheme,
            })
            .ToList();
    }

    /// <summary>列出 Theme Catalog 原始条目；包含不可选择的无效 Theme 诊断。</summary>
    public async Task<IReadOnlyList<ThemeCatalogItem>> ListThemeCatalogAsync(CancellationToken cancellationToken = default)
        => await _themeResolver.ListAvailableThemesAsync(cancellationToken).ConfigureAwait(false);

    private async Task WriteThemeConfigFileAsync(string themeId, string configurationJson, CancellationToken cancellationToken)
    {
        var path = ResolveThemeConfigPath(themeId);
        Directory.CreateDirectory(_layout.ThemeConfigDirectory);
        await File.WriteAllTextAsync(path, configurationJson, cancellationToken).ConfigureAwait(false);
    }

    private string ResolveThemeConfigPath(string themeId)
    {
        if (themeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            themeId.Contains('/') ||
            themeId.Contains('\\') ||
            string.Equals(themeId, ".", StringComparison.Ordinal) ||
            string.Equals(themeId, "..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Theme id '{themeId}' 不能作为 Theme 配置文件名。");
        }

        return Path.Combine(_layout.ThemeConfigDirectory, themeId + ".json");
    }

    private async Task<ThemeConfigurationRecord> GetOrCreateThemeRecordAsync(string themeId, CancellationToken cancellationToken)
    {
        var record = await _db.ThemeConfigurations
            .FirstOrDefaultAsync(x => x.ThemeId == themeId, cancellationToken)
            .ConfigureAwait(false);
        if (record is not null)
        {
            return record;
        }

        record = new ThemeConfigurationRecord
        {
            ThemeId = themeId,
            ConfigurationJson = "{}",
            I18nTextOverridesJson = "{}",
            UpdatedAt = _time.GetUtcNow(),
        };
        _db.ThemeConfigurations.Add(record);
        return record;
    }

    private async Task<ThemeManifest?> LoadThemeManifestAsync(string themeId, CancellationToken cancellationToken)
    {
        var loaded = await LoadThemeAsync(themeId, cancellationToken).ConfigureAwait(false);
        return loaded?.Manifest;
    }

    private async Task<ResolvedTheme?> LoadThemeAsync(string themeId, CancellationToken cancellationToken)
    {
        var result = await _themeResolver.ResolveThemeAsync(themeId, cancellationToken).ConfigureAwait(false);
        return result.Theme;
    }

    private static string ResolveRunnerKind(ThemeManifest manifest)
        => string.IsNullOrWhiteSpace(manifest.Runner?.Kind)
            ? (string.IsNullOrWhiteSpace(manifest.Build?.Command) ? "unknown" : "process")
            : manifest.Runner.Kind.Trim();

    private static string NormalizeThemeId(string themeId)
        => string.IsNullOrWhiteSpace(themeId) ? "bocchi-mono" : themeId.Trim();
}
