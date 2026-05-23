using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    /// <summary>Dashboard 解析 i18n://common@... display ref 时使用的最小 Common 默认文案。</summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> CommonDisplayDefaults = new(StringComparer.Ordinal)
    {
        ["page.normal.name"] = CreateLanguageValues("Normal", "普通页面", "普通頁面", "通常ページ"),
        ["menu.home"] = CreateLanguageValues("Home", "首页", "首頁", "ホーム"),
        ["menu.posts"] = CreateLanguageValues("Posts", "文章", "文章", "投稿"),
        ["menu.works"] = CreateLanguageValues("Works", "作品", "作品", "制作"),
        ["menu.notes"] = CreateLanguageValues("Notes", "札记", "札記", "ノート"),
        ["menu.friends"] = CreateLanguageValues("Friends", "友链", "友站", "リンク"),
    };

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

    /// <summary>创建 Dashboard display ref 默认多语言文案。</summary>
    private static Dictionary<string, string> CreateLanguageValues(
        string enUs,
        string zhCn,
        string zhTw,
        string jaJp)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = enUs,
            ["zh-CN"] = zhCn,
            ["zh-TW"] = zhTw,
            ["ja-JP"] = jaJp,
        };

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

    /// <summary>读取指定 Theme 的配置；缺失时返回空配置投影，不产生数据库写入。</summary>
    public async Task<ThemeConfigurationRecord> GetConfigurationAsync(
        string themeId,
        CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = NormalizeThemeId(themeId);
        var record = await _db.ThemeConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ThemeId == normalizedThemeId, cancellationToken)
            .ConfigureAwait(false);
        return record ?? new ThemeConfigurationRecord
        {
            ThemeId = normalizedThemeId,
            ConfigurationJson = "{}",
            I18nTextOverridesJson = "{}",
            UpdatedAt = _time.GetUtcNow(),
        };
    }

    /// <summary>保存当前默认 Theme 配置。</summary>
    public async Task SaveDefaultAsync(string themeId, string configurationJson, CancellationToken cancellationToken = default)
        => await SaveConfigurationAsync(themeId, configurationJson, cancellationToken).ConfigureAwait(false);

    /// <summary>保存指定 Theme 的 JSON 配置，并同步写入 DataRoot 中的 Theme 配置文件。</summary>
    public async Task SaveConfigurationAsync(
        string themeId,
        string configurationJson,
        CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = string.IsNullOrWhiteSpace(themeId) ? "default-static" : themeId.Trim();
        var normalizedJson = NormalizeConfigurationJson(configurationJson);
        var record = await GetOrCreateThemeRecordAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);

        record.ThemeId = normalizedThemeId;
        record.ConfigurationJson = normalizedJson;
        record.UpdatedAt = _time.GetUtcNow();
        await WriteThemeConfigFileAsync(normalizedThemeId, normalizedJson, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取当前 Theme 的 schema 定制视图，供 Dashboard 根据声明字段生成表单。</summary>
    public async Task<ThemeCustomizationView> GetCustomizationAsync(
        string themeId,
        CancellationToken cancellationToken = default)
    {
        var normalizedThemeId = NormalizeThemeId(themeId);
        var loadedTheme = await LoadThemeAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);
        var record = await GetConfigurationAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);
        var configuration = ParseConfigurationObject(record.ConfigurationJson);
        var groups = loadedTheme is null
            ? []
            : await LoadConfigGroupsAsync(loadedTheme.Value.ThemeRoot, configuration, cancellationToken).ConfigureAwait(false);

        return new ThemeCustomizationView
        {
            ThemeId = normalizedThemeId,
            ThemeName = loadedTheme is null ? normalizedThemeId : loadedTheme.Value.Manifest.Name,
            ConfigurationJson = record.ConfigurationJson,
            Groups = groups,
        };
    }

    /// <summary>保存主题定制页提交的 schema 字段值；未声明字段会被忽略，已有未知 JSON 键会保留。</summary>
    public async Task SaveCustomizationAsync(
        string themeId,
        IEnumerable<ThemeConfigValueInput> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var normalizedThemeId = NormalizeThemeId(themeId);
        var loadedTheme = await LoadThemeAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);
        if (loadedTheme is null)
        {
            return;
        }

        var record = await GetConfigurationAsync(normalizedThemeId, cancellationToken).ConfigureAwait(false);
        var configuration = ParseConfigurationObject(record.ConfigurationJson);
        var fields = (await LoadConfigGroupsAsync(loadedTheme.Value.ThemeRoot, new JsonObject(), cancellationToken).ConfigureAwait(false))
            .SelectMany(group => group.Fields)
            .ToDictionary(field => field.Key, StringComparer.Ordinal);
        var inputs = values
            .Where(value => !string.IsNullOrWhiteSpace(value.Key))
            .GroupBy(value => value.Key.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        foreach (var (key, field) in fields)
        {
            if (inputs.TryGetValue(key, out var input))
            {
                ApplySubmittedFieldValue(configuration, field, input);
            }
        }

        await SaveConfigurationAsync(
            normalizedThemeId,
            configuration.ToJsonString(JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>列出当前可选择的前台 Theme；内置默认 Theme 始终会被物化并排在第一位。</summary>
    public async Task<IReadOnlyList<ThemeOption>> ListAvailableThemesAsync(CancellationToken cancellationToken = default)
    {
        await DefaultStaticThemeDefinition.EnsureAsync(_layout.ThemesDirectory, cancellationToken).ConfigureAwait(false);

        var options = new Dictionary<string, ThemeOption>(StringComparer.Ordinal)
        {
            [DefaultStaticThemeDefinition.ThemeId] = new(
                DefaultStaticThemeDefinition.ThemeId,
                DefaultStaticThemeDefinition.ThemeName),
        };

        foreach (var themeDirectory in Directory.EnumerateDirectories(_layout.ThemesDirectory).Order(StringComparer.Ordinal))
        {
            var themeId = Path.GetFileName(themeDirectory);
            if (string.IsNullOrWhiteSpace(themeId) ||
                string.Equals(themeId, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal))
            {
                continue;
            }

            var loaded = await ThemeManifestLoader.TryLoadAsync(_layout.ThemesDirectory, themeId, cancellationToken)
                .ConfigureAwait(false);
            if (loaded is not null)
            {
                var manifest = loaded.Value.Manifest;
                options[manifest.Id] = new ThemeOption(manifest.Id, manifest.Name);
            }
        }

        return options.Values
            .OrderByDescending(x => string.Equals(x.Id, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        var loaded = await LoadThemeAsync(themeId, cancellationToken).ConfigureAwait(false);
        return loaded is null ? null : loaded.Value.Manifest;
    }

    private async Task<(ThemeManifest Manifest, string ThemeRoot)?> LoadThemeAsync(string themeId, CancellationToken cancellationToken)
    {
        if (string.Equals(themeId, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal))
        {
            await DefaultStaticThemeDefinition.EnsureAsync(_layout.ThemesDirectory, cancellationToken).ConfigureAwait(false);
        }

        return await ThemeManifestLoader.TryLoadAsync(_layout.ThemesDirectory, themeId, cancellationToken).ConfigureAwait(false);
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

    private static JsonObject ParseConfigurationObject(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(configurationJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static async Task<List<ThemeConfigGroupView>> LoadConfigGroupsAsync(
        string themeRoot,
        JsonObject configuration,
        CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(themeRoot, "config-schema.json");
        if (!File.Exists(schemaPath))
        {
            return [];
        }

        await using var stream = new FileStream(
            schemaPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        JsonNode? node;
        try
        {
            node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return [];
        }

        if (node?["groups"] is not JsonArray groups)
        {
            return [];
        }

        var result = new List<ThemeConfigGroupView>();
        foreach (var groupNode in groups.OfType<JsonObject>())
        {
            var id = ReadString(groupNode["id"]);
            var title = ReadString(groupNode["title"]);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var fields = groupNode["fields"] is JsonArray fieldArray
                ? fieldArray.OfType<JsonObject>()
                    .Select(field => MapConfigField(field, configuration))
                    .Where(field => field is not null)
                    .Cast<ThemeConfigFieldView>()
                    .ToList()
                : [];
            result.Add(new ThemeConfigGroupView
            {
                Id = id.Trim(),
                Title = title.Trim(),
                Fields = fields,
            });
        }

        return result;
    }

    private static ThemeConfigFieldView? MapConfigField(JsonObject field, JsonObject configuration)
    {
        var key = ReadString(field["key"]);
        var title = ReadString(field["title"]);
        var typeName = ReadString(field["type"]);
        if (string.IsNullOrWhiteSpace(key) ||
            string.IsNullOrWhiteSpace(title) ||
            !TryMapFieldType(typeName, out var type))
        {
            return null;
        }

        var defaultValue = field["default"];
        var savedValue = TryGetNestedValue(configuration, key);
        var currentValue = savedValue ?? defaultValue;
        return new ThemeConfigFieldView
        {
            Key = key.Trim(),
            Type = type,
            Title = title.Trim(),
            Description = TrimOrNull(ReadString(field["description"])),
            Placeholder = TrimOrNull(ReadString(field["placeholder"])),
            HelpText = TrimOrNull(ReadString(field["helpText"])),
            Required = ReadBool(field["required"]),
            Options = ReadStringOptions(field["options"]),
            TextValue = JsonNodeToText(currentValue),
            BooleanValue = JsonNodeToBool(currentValue),
            SelectedValues = JsonNodeToStringList(currentValue),
            LocalizedTextValues = JsonNodeToLocalizedText(savedValue),
            DefaultLocalizedTextValues = JsonNodeToLocalizedText(defaultValue),
            LocalizedTextListValues = JsonNodeToLocalizedTextList(savedValue),
            DefaultLocalizedTextListValues = JsonNodeToLocalizedTextList(defaultValue),
            DefaultText = TrimOrNull(JsonNodeToText(defaultValue)),
        };
    }

    private static void ApplySubmittedFieldValue(
        JsonObject configuration,
        ThemeConfigFieldView field,
        ThemeConfigValueInput input)
    {
        switch (field.Type)
        {
            case ThemeConfigFieldType.Boolean:
                SetNestedValue(configuration, field.Key, JsonValue.Create(ParseBoolean(input.Value)));
                break;
            case ThemeConfigFieldType.Number:
                ApplyNumberValue(configuration, field.Key, input.Value);
                break;
            case ThemeConfigFieldType.MultiSelect:
                ApplyMultiSelectValue(configuration, field, input.Values);
                break;
            case ThemeConfigFieldType.LocalizedText:
                ApplyLocalizedTextValue(configuration, field.Key, input.LocalizedValues);
                break;
            case ThemeConfigFieldType.LocalizedTextList:
                ApplyLocalizedTextListValue(configuration, field.Key, input.LocalizedListValues);
                break;
            case ThemeConfigFieldType.Select:
                ApplyStringValue(configuration, field, input.Value, validateOptions: true);
                break;
            case ThemeConfigFieldType.Group:
                break;
            default:
                ApplyStringValue(configuration, field, input.Value, validateOptions: false);
                break;
        }
    }

    private static void ApplyNumberValue(JsonObject configuration, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RemoveNestedValue(configuration, key);
            return;
        }

        if (!decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidOperationException($"Theme 配置字段 '{key}' 需要数字。");
        }

        SetNestedValue(configuration, key, JsonValue.Create(number));
    }

    private static void ApplyStringValue(
        JsonObject configuration,
        ThemeConfigFieldView field,
        string? value,
        bool validateOptions)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RemoveNestedValue(configuration, field.Key);
            return;
        }

        var normalized = value.Trim();
        if (validateOptions &&
            field.Options.Count > 0 &&
            !field.Options.Contains(normalized, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Theme 配置字段 '{field.Key}' 的选项无效。");
        }

        SetNestedValue(configuration, field.Key, JsonValue.Create(normalized));
    }

    private static void ApplyMultiSelectValue(
        JsonObject configuration,
        ThemeConfigFieldView field,
        IEnumerable<string> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => field.Options.Count == 0 || field.Options.Contains(value, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count == 0)
        {
            RemoveNestedValue(configuration, field.Key);
            return;
        }

        var array = new JsonArray();
        foreach (var value in normalized)
        {
            array.Add(value);
        }

        SetNestedValue(configuration, field.Key, array);
    }

    private static void ApplyLocalizedTextValue(
        JsonObject configuration,
        string key,
        IReadOnlyDictionary<string, string> values)
    {
        var normalized = values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new KeyValuePair<string, string>(pair.Key.Trim(), pair.Value.Trim()))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0)
        {
            RemoveNestedValue(configuration, key);
            return;
        }

        var obj = new JsonObject();
        foreach (var (language, value) in normalized.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            obj[language] = value;
        }

        SetNestedValue(configuration, key, obj);
    }

    private static void ApplyLocalizedTextListValue(
        JsonObject configuration,
        string key,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        var obj = new JsonObject();
        foreach (var (language, rawValues) in values.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)))
        {
            var normalized = rawValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (normalized.Count == 0)
            {
                continue;
            }

            var array = new JsonArray();
            foreach (var value in normalized)
            {
                array.Add(value);
            }

            obj[language.Trim()] = array;
        }

        if (obj.Count == 0)
        {
            RemoveNestedValue(configuration, key);
            return;
        }

        SetNestedValue(configuration, key, obj);
    }

    private static JsonNode? TryGetNestedValue(JsonObject root, string dottedKey)
    {
        var segments = SplitDottedKey(dottedKey);
        if (segments.Length == 0)
        {
            return null;
        }

        JsonNode? current = root;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static void SetNestedValue(JsonObject root, string dottedKey, JsonNode? value)
    {
        var segments = SplitDottedKey(dottedKey);
        if (segments.Length == 0)
        {
            return;
        }

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }

    private static void RemoveNestedValue(JsonObject root, string dottedKey)
    {
        var segments = SplitDottedKey(dottedKey);
        if (segments.Length == 0)
        {
            return;
        }

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                return;
            }

            current = child;
        }

        current.Remove(segments[^1]);
    }

    private static string[] SplitDottedKey(string dottedKey)
        => dottedKey.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryMapFieldType(string? value, out ThemeConfigFieldType type)
    {
        type = ThemeConfigFieldType.String;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() switch
        {
            "string" => SetType(ThemeConfigFieldType.String, out type),
            "number" => SetType(ThemeConfigFieldType.Number, out type),
            "boolean" => SetType(ThemeConfigFieldType.Boolean, out type),
            "select" => SetType(ThemeConfigFieldType.Select, out type),
            "multiSelect" => SetType(ThemeConfigFieldType.MultiSelect, out type),
            "color" => SetType(ThemeConfigFieldType.Color, out type),
            "image" => SetType(ThemeConfigFieldType.Image, out type),
            "url" => SetType(ThemeConfigFieldType.Url, out type),
            "localizedText" => SetType(ThemeConfigFieldType.LocalizedText, out type),
            "localizedTextList" => SetType(ThemeConfigFieldType.LocalizedTextList, out type),
            "group" => SetType(ThemeConfigFieldType.Group, out type),
            _ => Enum.TryParse(value, ignoreCase: true, out type),
        };
    }

    private static bool SetType(ThemeConfigFieldType value, out ThemeConfigFieldType type)
    {
        type = value;
        return true;
    }

    private static string? ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;

    private static bool ReadBool(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static bool ParseBoolean(string? value)
        => bool.TryParse(value, out var result) && result;

    private static List<string> ReadStringOptions(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(ReadString)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string JsonNodeToText(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : node.ToJsonString(JsonOptions);
    }

    private static bool JsonNodeToBool(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out boolean) && boolean;
    }

    private static List<string> JsonNodeToStringList(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            return array
                .Select(JsonNodeToText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        var single = JsonNodeToText(node);
        return string.IsNullOrWhiteSpace(single) ? [] : [single];
    }

    private static Dictionary<string, string> JsonNodeToLocalizedText(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return [];
        }

        return obj
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new KeyValuePair<string, string>(pair.Key.Trim(), JsonNodeToText(pair.Value).Trim()))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<string>> JsonNodeToLocalizedTextList(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return [];
        }

        return obj
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(pair.Key.Trim(), JsonNodeToStringList(pair.Value)))
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
