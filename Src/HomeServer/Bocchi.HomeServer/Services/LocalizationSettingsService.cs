using System.Text.Json;

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

    /// <summary>读取数据库记录；缺失时写入与默认内容工作区一致的本地化设置。</summary>
    private async Task<LocalizationSettingsRecord> GetOrCreateRecordAsync(CancellationToken cancellationToken)
    {
        var record = await _db.LocalizationSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
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
            UrlPolicy = PrimaryUnprefixedUrlPolicy,
            UpdatedAt = _time.GetUtcNow(),
        };
        _db.LocalizationSettings.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <summary>合并内置语言与自定义语言，保持内置语言优先。</summary>
    private static IReadOnlyList<LanguageRecord> MergeAvailableLanguages(IEnumerable<LanguageRecord> customLanguages)
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
    private static IReadOnlyList<LanguageRecord> NormalizeCustomLanguages(IEnumerable<LanguageRecord> customLanguages)
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
    private static IReadOnlyList<LanguageRecord> DeserializeLanguages(string json)
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

    /// <summary>以大小写不敏感方式比较语言代码。</summary>
    private static bool SameCode(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
