using System.Text.Json;

using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 管理站点本地化设置。workspace 的 <c>site/localization.yaml</c> 为权威；数据库保留投影。
/// 不处理 Dashboard UI language。形状世代跟 <c>site.yaml</c> 的 formatVersion。
/// </summary>
public sealed class LocalizationSettingsService
{
    /// <summary>默认站点主要语言，与工作区初始化的 <c>site.yaml</c> 保持一致。</summary>
    public const string DefaultPrimaryLanguage = "zh-CN";

    /// <summary>M6 固定 URL 策略：主语言无前缀，其他启用语言使用语言前缀。</summary>
    public const string PrimaryUnprefixedUrlPolicy = "PrimaryUnprefixed";

    /// <summary>Home Server 内置语言列表；自定义语言会追加到 Picklist 后面。</summary>
    public static IReadOnlyList<LanguageRecord> BuiltInLanguages { get; } =
    [
        new() { Code = "zh-CN", NativeName = "简体中文", EnglishName = "Simplified Chinese" },
        new() { Code = "en-US", NativeName = "English", EnglishName = "English" },
        new() { Code = "zh-TW", NativeName = "繁體中文", EnglishName = "Traditional Chinese" },
        new() { Code = "ja-JP", NativeName = "日本語", EnglishName = "Japanese" },
        new() { Code = "ko-KR", NativeName = "한국어", EnglishName = "Korean" },
        new() { Code = "fr-FR", NativeName = "Français", EnglishName = "French" },
        new() { Code = "de-DE", NativeName = "Deutsch", EnglishName = "German" },
        new() { Code = "es-ES", NativeName = "Español", EnglishName = "Spanish" },
    ];

    /// <summary>M6 首批 Common i18n key；Theme 可以使用这些 key，也可以选择不用。</summary>
    public static IReadOnlyList<string> CommonI18nKeys { get; } =
    [
        "menu.home",
        "menu.posts",
        "menu.works",
        "menu.notes",
        "menu.friends",
        "menu.about",
        "common.readMore",
        "common.backHome",
        "common.previous",
        "common.next",
        "content.translationNotice",
        "content.viewOriginal",
    ];

    /// <summary>本地化设置 JSON 使用 camelCase，后续进入 Theme Context 时不需要再迁移形态。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BocchiDbContext _db;
    private readonly BocchiDataLayout _layout;
    private readonly SiteProfileSettingsService _siteProfile;
    private readonly TimeProvider _time;
    private readonly ILogger<LocalizationSettingsService> _logger;

    /// <summary>构造站点本地化设置服务。</summary>
    public LocalizationSettingsService(
        BocchiDbContext db,
        BocchiDataLayout layout,
        SiteProfileSettingsService siteProfile,
        TimeProvider time,
        ILogger<LocalizationSettingsService> logger)
    {
        _db = db;
        _layout = layout;
        _siteProfile = siteProfile;
        _time = time;
        _logger = logger;
    }

    /// <summary>读取站点本地化设置；缺失时创建默认值。</summary>
    public async Task<LocalizationSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var record = await GetOrCreateRecordAsync(cancellationToken).ConfigureAwait(false);
        return ToView(record);
    }

    /// <summary>
    /// 启动同步：文件→DB；无文件且 DB 有数据则导出；两边都空则写最小默认。
    /// 坏文件不灌库；过新 site.yaml formatVersion 拒绝。
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        WorkspaceFormatVersion.EnsureExistingFileAllowsWrite(_layout.Workspace.SiteSettingsFile);

        var file = _layout.Workspace.LocalizationFile;
        if (File.Exists(file) && new FileInfo(file).Length > 0)
        {
            if (!TryLoadFromFile(file, out var snapshot, out var error))
            {
                _logger.LogError("localization.yaml 无效，跳过灌库，不覆盖文件：{Error}", error);
                return;
            }

            await ImportSnapshotIntoDbAsync(snapshot, cancellationToken).ConfigureAwait(false);
            await _siteProfile.SyncLanguageAsync(snapshot.PrimaryLanguage, cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await _db.LocalizationSettings
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is not null && HasMeaningfulLocalization(record))
        {
            var view = ToView(record);
            await WriteLocalizationYamlAsync(view, cancellationToken).ConfigureAwait(false);
            return;
        }

        var primaryFromSite = await ResolvePrimaryForDefaultAsync(cancellationToken).ConfigureAwait(false);
        var defaults = CreateDefaultView(primaryFromSite);
        await WriteLocalizationYamlAsync(defaults, cancellationToken).ConfigureAwait(false);
        await ImportSnapshotIntoDbAsync(SnapshotFromView(defaults), cancellationToken).ConfigureAwait(false);
        await _siteProfile.SyncLanguageAsync(defaults.PrimaryLanguage.Code, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>列出 Picklist 可用语言，内置语言在前，自定义语言在后。</summary>
    public async Task<IReadOnlyList<LanguageRecord>> ListAvailableLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        return MergeAvailableLanguages(settings.CustomLanguages);
    }

    /// <summary>保存站点主要语言、启用语言和自定义语言；先写 YAML，再写 DB，并同步 site.yaml language。</summary>
    public async Task SaveAsync(
        string primaryLanguageCode,
        IEnumerable<string> enabledLanguageCodes,
        IEnumerable<LanguageRecord> customLanguages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enabledLanguageCodes);
        ArgumentNullException.ThrowIfNull(customLanguages);
        WorkspaceFormatVersion.EnsureExistingFileAllowsWrite(_layout.Workspace.SiteSettingsFile);

        var custom = NormalizeCustomLanguages(customLanguages);
        var available = MergeAvailableLanguages(custom);
        var primary = ResolveLanguage(primaryLanguageCode, custom);
        var enabled = enabledLanguageCodes
            .Select(code => ResolveLanguage(code, custom))
            .DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!available.Any(x => SameCode(x.Code, primary.Code)))
        {
            custom = [.. custom, primary];
        }

        if (!enabled.Any(x => SameCode(x.Code, primary.Code)))
        {
            enabled.Add(primary);
        }

        if (enabled.Count == 0)
        {
            enabled.Add(primary);
        }

        var existing = await GetAsync(cancellationToken).ConfigureAwait(false);
        var view = new LocalizationSettingsView
        {
            PrimaryLanguage = primary,
            EnabledLanguages = enabled,
            CustomLanguages = custom,
            CommonTextOverrides = existing.CommonTextOverrides,
            UrlPolicy = PrimaryUnprefixedUrlPolicy,
        };

        await WriteLocalizationYamlAsync(view, cancellationToken).ConfigureAwait(false);

        var record = await GetOrCreateRecordAsync(cancellationToken).ConfigureAwait(false);
        record.PrimaryLanguage = primary.Code;
        record.EnabledLanguagesJson = JsonSerializer.Serialize(enabled, JsonOptions);
        record.CustomLanguagesJson = JsonSerializer.Serialize(custom, JsonOptions);
        record.UrlPolicy = PrimaryUnprefixedUrlPolicy;
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _siteProfile.SyncLanguageAsync(primary.Code, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存 Common i18n key 覆盖；先写 YAML，再写 DB。</summary>
    public async Task SaveCommonTextOverridesAsync(
        IEnumerable<CommonI18nTextOverride> textOverrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textOverrides);
        WorkspaceFormatVersion.EnsureExistingFileAllowsWrite(_layout.Workspace.SiteSettingsFile);

        var normalized = NormalizeCommonTextOverrides(textOverrides);
        var existing = await GetAsync(cancellationToken).ConfigureAwait(false);
        var view = new LocalizationSettingsView
        {
            PrimaryLanguage = existing.PrimaryLanguage,
            EnabledLanguages = existing.EnabledLanguages,
            CustomLanguages = existing.CustomLanguages,
            CommonTextOverrides = normalized,
            UrlPolicy = existing.UrlPolicy,
        };

        await WriteLocalizationYamlAsync(view, cancellationToken).ConfigureAwait(false);

        var record = await GetOrCreateRecordAsync(cancellationToken).ConfigureAwait(false);
        record.CommonTextOverridesJson = SerializeCommonTextOverrides(normalized);
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>生成构建时使用的本地化快照，避免 Generator 直接依赖 HomeServer EF Core。</summary>
    public async Task<BuildLocalizationOptions> GetBuildLocalizationOptionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        var text = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var item in settings.CommonTextOverrides)
        {
            text[item.Key] = new Dictionary<string, string>(item.Values, StringComparer.OrdinalIgnoreCase);
        }

        return new BuildLocalizationOptions
        {
            PrimaryLanguage = settings.PrimaryLanguage.Code,
            EnabledLanguages = settings.EnabledLanguages.Select(ToBuildLanguageRecord).ToArray(),
            UrlPolicy = settings.UrlPolicy,
            Text = text,
        };
    }

    private async Task<string> ResolvePrimaryForDefaultAsync(CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _siteProfile.GetAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(profile.Language))
            {
                return profile.Language.Trim();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            // 启动早期 DB 未就绪时退回默认。
        }

        var siteYaml = _layout.Workspace.SiteSettingsFile;
        if (File.Exists(siteYaml))
        {
            try
            {
                var raw = await File.ReadAllTextAsync(siteYaml, cancellationToken).ConfigureAwait(false);
                var stream = new YamlStream();
                using var reader = new StringReader(raw);
                stream.Load(reader);
                if (stream.Documents.Count > 0
                    && stream.Documents[0].RootNode is YamlMappingNode root
                    && root.Children.TryGetValue(new YamlScalarNode("language"), out var node)
                    && node is YamlScalarNode scalar
                    && !string.IsNullOrWhiteSpace(scalar.Value))
                {
                    return scalar.Value.Trim();
                }
            }
            catch (YamlException)
            {
                // ignore
            }
            catch (IOException)
            {
                // ignore
            }
        }

        return DefaultPrimaryLanguage;
    }

    private static LocalizationSettingsView CreateDefaultView(string primaryCode)
    {
        var primary = ResolveLanguage(primaryCode, []);
        return new LocalizationSettingsView
        {
            PrimaryLanguage = primary,
            EnabledLanguages = [primary],
            CustomLanguages = [],
            CommonTextOverrides = [],
            UrlPolicy = PrimaryUnprefixedUrlPolicy,
        };
    }

    private static bool HasMeaningfulLocalization(LocalizationSettingsRecord record)
    {
        if (!string.Equals(record.PrimaryLanguage, DefaultPrimaryLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var enabled = DeserializeLanguages(record.EnabledLanguagesJson);
        if (enabled.Count > 1
            || (enabled.Count == 1 && !SameCode(enabled[0].Code, DefaultPrimaryLanguage)))
        {
            return true;
        }

        if (DeserializeLanguages(record.CustomLanguagesJson).Count > 0)
        {
            return true;
        }

        return DeserializeCommonTextOverrides(record.CommonTextOverridesJson).Count > 0;
    }

    private async Task ImportSnapshotIntoDbAsync(LocalizationFileSnapshot snapshot, CancellationToken cancellationToken)
    {
        var record = await GetOrCreateRecordAsync(cancellationToken).ConfigureAwait(false);
        record.PrimaryLanguage = snapshot.PrimaryLanguage;
        record.EnabledLanguagesJson = JsonSerializer.Serialize(snapshot.EnabledLanguages, JsonOptions);
        record.CustomLanguagesJson = JsonSerializer.Serialize(snapshot.CustomLanguages, JsonOptions);
        record.CommonTextOverridesJson = SerializeCommonTextOverrides(snapshot.CommonTextOverrides);
        record.UrlPolicy = string.IsNullOrWhiteSpace(snapshot.UrlPolicy)
            ? PrimaryUnprefixedUrlPolicy
            : snapshot.UrlPolicy;
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteLocalizationYamlAsync(LocalizationSettingsView view, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_layout.Workspace.SiteDirectory);
        var root = new YamlMappingNode
        {
            { new YamlScalarNode("primaryLanguage"), new YamlScalarNode(view.PrimaryLanguage.Code) },
            { new YamlScalarNode("urlPolicy"), new YamlScalarNode(view.UrlPolicy) },
            { new YamlScalarNode("enabledLanguages"), BuildLanguagesYaml(view.EnabledLanguages) },
            { new YamlScalarNode("customLanguages"), BuildLanguagesYaml(view.CustomLanguages) },
            { new YamlScalarNode("commonTextOverrides"), BuildCommonTextOverridesYaml(view.CommonTextOverrides) },
        };
        var stream = new YamlStream(new YamlDocument(root));
        await using var output = File.Create(_layout.Workspace.LocalizationFile);
        await using var writer = new StreamWriter(output);
        stream.Save(writer, assignAnchors: false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static YamlSequenceNode BuildLanguagesYaml(IEnumerable<LanguageRecord> languages)
    {
        var seq = new YamlSequenceNode();
        foreach (var language in languages)
        {
            seq.Add(new YamlMappingNode
            {
                { new YamlScalarNode("code"), new YamlScalarNode(language.Code) },
                { new YamlScalarNode("nativeName"), new YamlScalarNode(language.NativeName) },
                { new YamlScalarNode("englishName"), new YamlScalarNode(language.EnglishName) },
            });
        }

        return seq;
    }

    private static YamlMappingNode BuildCommonTextOverridesYaml(IEnumerable<CommonI18nTextOverride> overrides)
    {
        var root = new YamlMappingNode();
        foreach (var item in overrides.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var langs = new YamlMappingNode();
            foreach (var pair in item.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                langs.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));
            }

            root.Add(new YamlScalarNode(item.Key), langs);
        }

        return root;
    }

    private static bool TryLoadFromFile(string file, out LocalizationFileSnapshot snapshot, out string error)
    {
        snapshot = default!;
        error = string.Empty;
        string raw;
        try
        {
            raw = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "文件为空。";
            return false;
        }

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(raw);
            stream.Load(reader);
            if (stream.Documents.Count == 0
                || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                error = "根节点必须是 mapping。";
                return false;
            }

            var primaryRaw = ReadScalar(root, "primaryLanguage");
            if (string.IsNullOrWhiteSpace(primaryRaw))
            {
                error = "缺少 primaryLanguage。";
                return false;
            }

            if (!TryGetSequence(root, "enabledLanguages", out var enabledSeq) || enabledSeq is null)
            {
                error = "缺少 enabledLanguages。";
                return false;
            }

            var customSeq = TryGetSequence(root, "customLanguages", out var cs) ? cs : new YamlSequenceNode();
            var custom = NormalizeCustomLanguages(ParseLanguages(customSeq!));
            var primary = ResolveLanguage(primaryRaw, custom);
            var enabled = ParseLanguages(enabledSeq)
                .Select(x => ResolveLanguage(x.Code, custom))
                .DistinctBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!enabled.Any(x => SameCode(x.Code, primary.Code)))
            {
                enabled.Add(primary);
            }

            var urlPolicy = ReadScalar(root, "urlPolicy");
            if (string.IsNullOrWhiteSpace(urlPolicy))
            {
                urlPolicy = PrimaryUnprefixedUrlPolicy;
            }

            var overrides = ParseCommonTextOverrides(root);
            snapshot = new LocalizationFileSnapshot(
                primary.Code,
                enabled,
                custom,
                overrides,
                urlPolicy.Trim());
            return true;
        }
        catch (YamlException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static List<LanguageRecord> ParseLanguages(YamlSequenceNode seq)
    {
        var result = new List<LanguageRecord>();
        foreach (var child in seq.Children)
        {
            if (child is not YamlMappingNode map)
            {
                continue;
            }

            var code = ReadScalar(map, "code");
            var nativeName = ReadScalar(map, "nativeName");
            var englishName = ReadScalar(map, "englishName");
            if (string.IsNullOrWhiteSpace(code)
                || string.IsNullOrWhiteSpace(nativeName)
                || string.IsNullOrWhiteSpace(englishName))
            {
                continue;
            }

            result.Add(new LanguageRecord
            {
                Code = code.Trim(),
                NativeName = nativeName.Trim(),
                EnglishName = englishName.Trim(),
            });
        }

        return result;
    }

    private static List<CommonI18nTextOverride> ParseCommonTextOverrides(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("commonTextOverrides"), out var node)
            || node is not YamlMappingNode map)
        {
            return [];
        }

        var raw = new List<CommonI18nTextOverride>();
        foreach (var (keyNode, valueNode) in map.Children)
        {
            if (keyNode is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value)
                || valueNode is not YamlMappingNode langs)
            {
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (langKey, langVal) in langs.Children)
            {
                if (langKey is not YamlScalarNode lk || langVal is not YamlScalarNode lv
                    || string.IsNullOrWhiteSpace(lk.Value) || string.IsNullOrWhiteSpace(lv.Value))
                {
                    continue;
                }

                values[lk.Value.Trim()] = lv.Value.Trim();
            }

            if (values.Count > 0)
            {
                raw.Add(new CommonI18nTextOverride { Key = key.Value.Trim(), Values = values });
            }
        }

        return NormalizeCommonTextOverrides(raw);
    }

    private async Task<LocalizationSettingsRecord> GetOrCreateRecordAsync(CancellationToken cancellationToken)
    {
        var record = await _db.LocalizationSettings
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (record is not null)
        {
            return record;
        }

        var primary = BuiltInLanguages.First(x => SameCode(x.Code, DefaultPrimaryLanguage));
        record = new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = primary.Code,
            EnabledLanguagesJson = JsonSerializer.Serialize(new[] { primary }, JsonOptions),
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = "{}",
            UrlPolicy = PrimaryUnprefixedUrlPolicy,
            UpdatedAt = _time.GetUtcNow(),
        };
        _db.LocalizationSettings.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    private static LocalizationSettingsView ToView(LocalizationSettingsRecord record)
    {
        var custom = DeserializeLanguages(record.CustomLanguagesJson);
        var enabled = DeserializeLanguages(record.EnabledLanguagesJson);
        var textOverrides = DeserializeCommonTextOverrides(record.CommonTextOverridesJson);
        var primary = ResolveLanguage(record.PrimaryLanguage, custom);
        if (!enabled.Any(x => SameCode(x.Code, primary.Code)))
        {
            enabled = [.. enabled, primary];
        }

        return new LocalizationSettingsView
        {
            PrimaryLanguage = primary,
            EnabledLanguages = enabled,
            CustomLanguages = custom,
            CommonTextOverrides = textOverrides,
            UrlPolicy = string.IsNullOrWhiteSpace(record.UrlPolicy) ? PrimaryUnprefixedUrlPolicy : record.UrlPolicy,
        };
    }

    private static LocalizationFileSnapshot SnapshotFromView(LocalizationSettingsView view)
        => new(
            view.PrimaryLanguage.Code,
            view.EnabledLanguages.ToList(),
            view.CustomLanguages.ToList(),
            view.CommonTextOverrides.ToList(),
            view.UrlPolicy);

    private static List<LanguageRecord> MergeAvailableLanguages(IEnumerable<LanguageRecord> customLanguages)
        => [.. BuiltInLanguages, .. NormalizeCustomLanguages(customLanguages)];

    private static LanguageRecord ResolveLanguage(string? languageCode, IEnumerable<LanguageRecord> customLanguages)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? DefaultPrimaryLanguage : languageCode.Trim();
        var available = MergeAvailableLanguages(customLanguages);
        return available.FirstOrDefault(x => SameCode(x.Code, normalized))
            ?? new LanguageRecord { Code = normalized, NativeName = normalized, EnglishName = normalized };
    }

    private static List<LanguageRecord> NormalizeCustomLanguages(IEnumerable<LanguageRecord> customLanguages)
    {
        var result = new List<LanguageRecord>();
        var seen = new HashSet<string>(BuiltInLanguages.Select(x => x.Code), StringComparer.OrdinalIgnoreCase);
        foreach (var language in customLanguages)
        {
            if (string.IsNullOrWhiteSpace(language.Code)
                || string.IsNullOrWhiteSpace(language.NativeName)
                || string.IsNullOrWhiteSpace(language.EnglishName))
            {
                continue;
            }

            var code = language.Code.Trim();
            if (!seen.Add(code))
            {
                continue;
            }

            result.Add(new LanguageRecord
            {
                Code = code,
                NativeName = language.NativeName.Trim(),
                EnglishName = language.EnglishName.Trim(),
            });
        }

        return result;
    }

    private static List<LanguageRecord> DeserializeLanguages(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<LanguageRecord>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<CommonI18nTextOverride> DeserializeCommonTextOverrides(string json)
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

            return NormalizeCommonTextOverrides(raw.Select(x => new CommonI18nTextOverride
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

    private static List<CommonI18nTextOverride> NormalizeCommonTextOverrides(IEnumerable<CommonI18nTextOverride> textOverrides)
    {
        var result = new List<CommonI18nTextOverride>();
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
                result.Add(new CommonI18nTextOverride
                {
                    Key = item.Key.Trim(),
                    Values = values,
                });
            }
        }

        return result;
    }

    private static string SerializeCommonTextOverrides(IEnumerable<CommonI18nTextOverride> textOverrides)
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

    private static BuildLanguageRecord ToBuildLanguageRecord(LanguageRecord language)
        => new()
        {
            Code = language.Code,
            NativeName = language.NativeName,
            EnglishName = language.EnglishName,
        };

    private static bool SameCode(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string? ReadScalar(YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static bool TryGetSequence(YamlMappingNode map, string key, out YamlSequenceNode? seq)
    {
        seq = null;
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return false;
        }

        if (node is YamlSequenceNode sequence)
        {
            seq = sequence;
            return true;
        }

        return false;
    }

    private sealed record LocalizationFileSnapshot(
        string PrimaryLanguage,
        List<LanguageRecord> EnabledLanguages,
        List<LanguageRecord> CustomLanguages,
        List<CommonI18nTextOverride> CommonTextOverrides,
        string UrlPolicy);
}
