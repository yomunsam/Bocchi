using System.Text.Json;

using Bocchi.GeneratorContract;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>Theme 私有 i18n manifest、覆盖值与构建快照处理。</summary>
public sealed partial class ThemeSettingsService
{
    /// <summary>读取当前 Theme manifest 声明的私有 i18n key 和用户覆盖值。</summary>
    public async Task<ThemeI18nSettingsView> GetI18nAsync(string themeId, CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = NormalizeThemeId(themeId);
        var manifest = await LoadThemeManifestAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);
        var record = await _db.ThemeConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ThemeId == normalizedThemeId, cancellationToken)
            .ConfigureAwait(false);
        var overrides = DeserializeTextOverrides(record?.I18nTextOverridesJson ?? "{}");
        return new ThemeI18nSettingsView
        {
            ThemeId = normalizedThemeId,
            SupportedLanguages = NormalizeLanguageCodes(manifest?.I18n?.SupportedLanguages ?? []),
            DefaultLanguage = string.IsNullOrWhiteSpace(manifest?.I18n?.DefaultLanguage)
                ? null
                : manifest.I18n.DefaultLanguage.Trim(),
            Keys = MapI18nKeys(manifest?.I18n?.Keys ?? []),
            TextOverrides = overrides,
        };
    }

    /// <summary>保存当前 Theme 的私有 i18n 覆盖；只保存用户填写的 plain text 值。</summary>
    public async Task SaveI18nTextOverridesAsync(
        string themeId,
        IEnumerable<ThemeI18nTextOverride> textOverrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textOverrides);

        var normalizedThemeId = NormalizeThemeId(themeId);
        var normalized = NormalizeTextOverrides(textOverrides);
        var record = await GetOrCreateThemeRecordAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);
        record.I18nTextOverridesJson = SerializeTextOverrides(normalized);
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取构建快照需要的 Theme 私有 i18n 覆盖，保持 HomeServer EF Core 不进入 Generator。</summary>
    public async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> GetBuildI18nTextOverridesAsync(
        string themeId,
        CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = NormalizeThemeId(themeId);
        var record = await _db.ThemeConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ThemeId == normalizedThemeId, cancellationToken)
            .ConfigureAwait(false);
        var overrides = DeserializeTextOverrides(record?.I18nTextOverridesJson ?? "{}");
        return overrides.ToDictionary(
            x => x.Key,
            x => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(x.Values, StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);
    }

    private static List<string> NormalizeLanguageCodes(IEnumerable<string> languageCodes)
        => languageCodes
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Select(language => language.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<ThemeI18nKeyView> MapI18nKeys(IEnumerable<ThemeI18nKeyManifest> keys)
        => keys
            .Where(key => !string.IsNullOrWhiteSpace(key.Key) && !string.IsNullOrWhiteSpace(key.Title))
            .GroupBy(key => key.Key.Trim(), StringComparer.Ordinal)
            .Select(group => MapI18nKey(group.First()))
            .OrderBy(key => key.Key, StringComparer.Ordinal)
            .ToList();

    private static ThemeI18nKeyView MapI18nKey(ThemeI18nKeyManifest key)
        => new()
        {
            Key = key.Key.Trim(),
            Title = key.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(key.Description) ? null : key.Description.Trim(),
            DefaultValues = key.DefaultValues
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key.Trim(), x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase),
        };

    private static List<ThemeI18nTextOverride> DeserializeTextOverrides(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json, JsonOptions);
            if (raw is null)
            {
                return [];
            }

            return NormalizeTextOverrides(raw.Select(x => new ThemeI18nTextOverride
            {
                Key = x.Key,
                Values = x.Value,
            }));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<ThemeI18nTextOverride> NormalizeTextOverrides(IEnumerable<ThemeI18nTextOverride> textOverrides)
    {
        var result = new List<ThemeI18nTextOverride>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in textOverrides)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || !seenKeys.Add(item.Key.Trim()))
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (language, value) in item.Values)
            {
                if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                values[language.Trim()] = value.Trim();
            }

            if (values.Count > 0)
            {
                result.Add(new ThemeI18nTextOverride
                {
                    Key = item.Key.Trim(),
                    Values = values,
                });
            }
        }

        return result;
    }

    private static string SerializeTextOverrides(IEnumerable<ThemeI18nTextOverride> textOverrides)
    {
        var root = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var item in textOverrides.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            root[item.Key] = item.Values
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Serialize(root, JsonOptions);
    }
}
