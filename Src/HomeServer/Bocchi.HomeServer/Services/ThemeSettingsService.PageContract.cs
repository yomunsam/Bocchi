using Bocchi.GeneratorContract;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>Theme Page Contract 展示模型与 display ref 解析。</summary>
public sealed partial class ThemeSettingsService
{
    /// <summary>读取当前 Theme 的 Page 模板和特殊页面声明，Dashboard 用它渲染 Page 编辑器和 Menu target。</summary>
    public async Task<ThemePageContractView> GetPageContractAsync(
        string themeId,
        string dashboardLanguage,
        CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = NormalizeThemeId(themeId);
        var manifest = await LoadThemeManifestAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);
        var record = await _db.ThemeConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ThemeId == normalizedThemeId, cancellationToken)
            .ConfigureAwait(false);
        var overrides = DeserializeTextOverrides(record?.I18nTextOverridesJson ?? "{}")
            .ToDictionary(x => x.Key, x => (IReadOnlyDictionary<string, string>)x.Values, StringComparer.Ordinal);
        return new ThemePageContractView
        {
            ThemeId = normalizedThemeId,
            ThemeName = manifest?.Name ?? normalizedThemeId,
            PageTemplates = NormalizePageTemplates(manifest)
                .Select(template => new ThemePageTemplateOption
                {
                    Name = template.Name,
                    RawDisplayName = template.DisplayName,
                    DisplayName = ResolveDisplayName(template.DisplayName, manifest, overrides, dashboardLanguage),
                })
                .ToArray(),
            SpecialPages = NormalizeSpecialPages(manifest)
                .Select(page => new ThemeSpecialPageOption
                {
                    Name = page.Name,
                    RawDisplayName = page.DisplayName,
                    DisplayName = ResolveDisplayName(page.DisplayName, manifest, overrides, dashboardLanguage),
                    Route = page.Route,
                })
                .ToArray(),
        };
    }

    private static List<ThemePageTemplateManifest> NormalizePageTemplates(ThemeManifest? manifest)
    {
        var result = new List<ThemePageTemplateManifest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in manifest?.PageTemplates ?? [])
        {
            if (string.IsNullOrWhiteSpace(template.Name) ||
                string.IsNullOrWhiteSpace(template.DisplayName) ||
                !seen.Add(template.Name.Trim()))
            {
                continue;
            }

            result.Add(new ThemePageTemplateManifest
            {
                Name = template.Name.Trim(),
                DisplayName = template.DisplayName.Trim(),
            });
        }

        if (!seen.Contains("normal"))
        {
            result.Insert(0, new ThemePageTemplateManifest
            {
                Name = "normal",
                DisplayName = "i18n://common@page.normal.name",
            });
        }

        return result;
    }

    private static List<ThemeSpecialPageManifest> NormalizeSpecialPages(ThemeManifest? manifest)
    {
        var result = new List<ThemeSpecialPageManifest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in manifest?.SpecialPages ?? [])
        {
            if (string.IsNullOrWhiteSpace(page.Name) ||
                string.IsNullOrWhiteSpace(page.DisplayName) ||
                string.IsNullOrWhiteSpace(page.Route) ||
                page.Route.Trim()[0] != '/' ||
                page.Route.Contains("..", StringComparison.Ordinal) ||
                !seen.Add(page.Name.Trim()))
            {
                continue;
            }

            result.Add(new ThemeSpecialPageManifest
            {
                Name = page.Name.Trim(),
                DisplayName = page.DisplayName.Trim(),
                Route = page.Route.Trim(),
            });
        }

        return result;
    }

    private static string ResolveDisplayName(
        string raw,
        ThemeManifest? manifest,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> themeOverrides,
        string language)
    {
        if (!TryParseDisplayRef(raw, out var scope, out var key))
        {
            return raw;
        }

        if (string.Equals(scope, "theme", StringComparison.Ordinal))
        {
            return ResolveLanguageValue(themeOverrides, key, language)
                ?? ResolveThemeDefaultValue(manifest, key, language)
                ?? key;
        }

        if (string.Equals(scope, "common", StringComparison.Ordinal))
        {
            return ResolveLanguageValue(CommonDisplayDefaults, key, language) ?? key;
        }

        return key;
    }

    private static string? ResolveLanguageValue(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values,
        string key,
        string language)
    {
        if (!values.TryGetValue(key, out var languageValues))
        {
            return null;
        }

        if (languageValues.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return languageValues.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? ResolveThemeDefaultValue(ThemeManifest? manifest, string key, string language)
    {
        var item = manifest?.I18n?.Keys.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (item is null)
        {
            return null;
        }

        if (item.DefaultValues.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(manifest?.I18n?.DefaultLanguage) &&
            item.DefaultValues.TryGetValue(manifest.I18n.DefaultLanguage, out var fallback) &&
            !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return item.DefaultValues.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static bool TryParseDisplayRef(string value, out string scope, out string key)
    {
        scope = string.Empty;
        key = string.Empty;
        const string prefix = "i18n://";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = value[prefix.Length..];
        var separator = body.IndexOf('@', StringComparison.Ordinal);
        if (separator <= 0 || separator >= body.Length - 1)
        {
            return false;
        }

        scope = body[..separator].Trim();
        key = body[(separator + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(key);
    }

}
