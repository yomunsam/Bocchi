using System.Text.Json;

using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 管理站点本地化设置。它处理 Site primary language、Site enabled languages 与自定义语言，不处理 Dashboard UI language。
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

    /// <summary>Home Server 数据库上下文。</summary>
    private readonly BocchiDbContext _db;

    /// <summary>可测试时间源，用于写入更新时间。</summary>
    private readonly TimeProvider _time;

    /// <summary>构造站点本地化设置服务。</summary>
    public LocalizationSettingsService(BocchiDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>读取站点本地化设置；缺失时创建默认值。</summary>
    public async Task<LocalizationSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var record = await GetOrCreateRecordAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>列出 Picklist 可用语言，内置语言在前，自定义语言在后。</summary>
    public async Task<IReadOnlyList<LanguageRecord>> ListAvailableLanguagesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        return MergeAvailableLanguages(settings.CustomLanguages);
    }

    /// <summary>保存站点主要语言、启用语言和自定义语言；服务层会自动确保启用语言包含主要语言。</summary>
    public async Task SaveAsync(
        string primaryLanguageCode,
        IEnumerable<string> enabledLanguageCodes,
        IEnumerable<LanguageRecord> customLanguages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enabledLanguageCodes);
        ArgumentNullException.ThrowIfNull(customLanguages);

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

        var record = await GetOrCreateRecordAsync(cancellationToken).ConfigureAwait(false);
        record.PrimaryLanguage = primary.Code;
        record.EnabledLanguagesJson = JsonSerializer.Serialize(enabled, JsonOptions);
        record.CustomLanguagesJson = JsonSerializer.Serialize(custom, JsonOptions);
        record.UrlPolicy = PrimaryUnprefixedUrlPolicy;
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存 Common i18n key 覆盖；只持久化用户填写的 plain text 值。</summary>
    public async Task SaveCommonTextOverridesAsync(
        IEnumerable<CommonI18nTextOverride> textOverrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textOverrides);

        var normalized = NormalizeCommonTextOverrides(textOverrides);
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

    /// <summary>读取数据库记录；缺失时写入与默认内容工作区一致的本地化设置。</summary>
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

    /// <summary>合并内置语言与自定义语言，保持内置语言优先。</summary>
    private static List<LanguageRecord> MergeAvailableLanguages(IEnumerable<LanguageRecord> customLanguages)
        => [.. BuiltInLanguages, .. NormalizeCustomLanguages(customLanguages)];

    /// <summary>把语言代码解析为 Language record；未知代码保留为自描述自定义语言。</summary>
    private static LanguageRecord ResolveLanguage(string? languageCode, IEnumerable<LanguageRecord> customLanguages)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? DefaultPrimaryLanguage : languageCode.Trim();
        var available = MergeAvailableLanguages(customLanguages);
        return available.FirstOrDefault(x => SameCode(x.Code, normalized))
            ?? new LanguageRecord { Code = normalized, NativeName = normalized, EnglishName = normalized };
    }

    /// <summary>清理自定义语言输入，丢弃不完整项并避免覆盖内置语言。</summary>
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

    /// <summary>反序列化语言 JSON；损坏时返回空列表，让设置页可用默认值恢复。</summary>
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

    /// <summary>反序列化 Common i18n 覆盖；损坏时返回空列表，避免 Settings 页面打不开。</summary>
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

    /// <summary>清理 Common i18n 覆盖，丢弃空 key、空语言和空值。</summary>
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

    /// <summary>序列化 Common i18n 覆盖，按 key 与语言排序以稳定构建指纹。</summary>
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

    /// <summary>把 HomeServer 语言记录转换为 Generator 可消费的中性构建快照。</summary>
    private static BuildLanguageRecord ToBuildLanguageRecord(LanguageRecord language)
        => new()
        {
            Code = language.Code,
            NativeName = language.NativeName,
            EnglishName = language.EnglishName,
        };

    /// <summary>以大小写不敏感方式比较语言代码。</summary>
    private static bool SameCode(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
