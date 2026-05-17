using System.Text.Json;

using Bocchi.Generator.Theme;
using Bocchi.GeneratorContract;
using Bocchi.HomeServer.Data;
using Bocchi.Theme.DefaultStatic;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 前台业务 Theme 配置服务。它处理 Theme Contract 配置，不处理 Dashboard 明暗外观。
/// </summary>
public sealed class ThemeSettingsService
{
    /// <summary>Theme 设置 JSON 使用 camelCase，与 theme.json 和 Theme Context 保持一致。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BocchiDbContext _db;
    private readonly TimeProvider _time;
    private readonly BocchiDataLayout _layout;

    /// <summary>构造 Theme 设置服务。</summary>
    public ThemeSettingsService(BocchiDbContext db, TimeProvider time, BocchiDataLayout layout)
    {
        _db = db;
        _time = time;
        _layout = layout;
    }

    /// <summary>读取当前默认 Theme 配置；没有配置时返回一个可编辑空配置。</summary>
    public async Task<ThemeConfigurationRecord> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var record = await _db.ThemeConfigurations
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return record ?? new ThemeConfigurationRecord
        {
            ThemeId = "default-static",
            ConfigurationJson = "{}",
            I18nTextOverridesJson = "{}",
            UpdatedAt = _time.GetUtcNow(),
        };
    }

    /// <summary>保存当前默认 Theme 配置。</summary>
    public async Task SaveDefaultAsync(string themeId, string configurationJson, CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = string.IsNullOrWhiteSpace(themeId) ? "default-static" : themeId.Trim();
        var normalizedJson = NormalizeConfigurationJson(configurationJson);
        var record = await _db.ThemeConfigurations
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new ThemeConfigurationRecord();
            _db.ThemeConfigurations.Add(record);
        }

        record.ThemeId = normalizedThemeId;
        record.ConfigurationJson = normalizedJson;
        record.UpdatedAt = _time.GetUtcNow();
        await WriteThemeConfigFileAsync(normalizedThemeId, normalizedJson, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

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
        if (string.Equals(themeId, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal))
        {
            await DefaultStaticThemeDefinition.EnsureAsync(_layout.ThemesDirectory, cancellationToken).ConfigureAwait(false);
        }

        var loaded = await ThemeManifestLoader.TryLoadAsync(_layout.ThemesDirectory, themeId, cancellationToken).ConfigureAwait(false);
        return loaded?.Manifest;
    }

    private static string NormalizeThemeId(string themeId)
        => string.IsNullOrWhiteSpace(themeId) ? "default-static" : themeId.Trim();

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

    private static string NormalizeConfigurationJson(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(configurationJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Theme 配置必须是 JSON object。");
        }

        return document.RootElement.GetRawText();
    }

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
