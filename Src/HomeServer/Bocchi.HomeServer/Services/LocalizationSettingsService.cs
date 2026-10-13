using System.Text.Json;

using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 管理站点本地化设置。<c>site/localization.yaml</c> 是前台语言的权威来源，数据库只留投影。
/// 它不处理 Dashboard UI language。保存时把 <c>primaryLanguage</c> 同步到 <c>site.yaml</c> 的 <c>language</c>。
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

    /// <summary>workspace 路径。本地化文件和 <c>site.yaml</c> 版本闸门都从这里取。</summary>
    private readonly BocchiDataLayout _layout;

    /// <summary>同步 <c>site.yaml</c> 的 <c>language</c>，避免两处主语言各写各的。</summary>
    private readonly SiteProfileSettingsService _siteProfile;

    /// <summary>可测试时间源，用于写入更新时间。</summary>
    private readonly TimeProvider _time;

    /// <summary>同步被拒绝或文件损坏时的诊断。</summary>
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

    /// <summary>
    /// 保存站点主要语言、启用语言和自定义语言。先写 <c>localization.yaml</c>，再写数据库，并同步 <c>site.yaml</c> 的 <c>language</c>。
    /// 版本过新或现有文件无效时两边都不改。
    /// </summary>
    public async Task<WorkspaceWriteResult> SaveAsync(
        string primaryLanguageCode,
        IEnumerable<string> enabledLanguageCodes,
        IEnumerable<LanguageRecord> customLanguages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enabledLanguageCodes);
        ArgumentNullException.ThrowIfNull(customLanguages);

        var refusal = await PeekSaveRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            _logger.LogError("本地化保存已拒绝：{Message}", refusal.Message);
            return refusal;
        }

        var state = NormalizeForSave(primaryLanguageCode, enabledLanguageCodes, customLanguages, ReadStoredOverrides(await FindRecordAsync(cancellationToken).ConfigureAwait(false)));
        return await CommitAsync(state, syncLanguage: true, warnOnConflict: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存 Common i18n key 覆盖。先写 YAML 再写数据库，避免只改投影。</summary>
    public async Task<WorkspaceWriteResult> SaveCommonTextOverridesAsync(
        IEnumerable<CommonI18nTextOverride> textOverrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textOverrides);

        var refusal = await PeekSaveRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            _logger.LogError("Common 文案保存已拒绝：{Message}", refusal.Message);
            return refusal;
        }

        var current = await ReadProjectionAsync(cancellationToken).ConfigureAwait(false);
        var state = current with { Overrides = NormalizeCommonTextOverrides(textOverrides) };
        return await CommitAsync(state, syncLanguage: false, warnOnConflict: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 启动同步。有效文件灌库；没有文件而数据库有记录则导出一次；两边都空则写 zh-CN 默认文件。
    /// 导出时 <c>primaryLanguage</c> 优先用 <c>site.yaml</c> 的 <c>language</c>。
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        var refusal = await PeekSaveRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            _logger.LogError("本地化同步已跳过：{Message}", refusal.Message);
            return;
        }

        var document = await LocalizationYaml.ReadAsync(_layout.Workspace.LocalizationFile, cancellationToken).ConfigureAwait(false);
        // 无效文件不能走导入：缺 primaryLanguage 会被收成 zh-CN 默认并写回，盖掉原来的坏文件。
        if (document.ErrorCode is not null)
        {
            _logger.LogError("本地化同步已跳过：{Message}", document.ErrorMessage);
            return;
        }

        if (!document.IsEmpty)
        {
            var imported = NormalizeImported(document);
            if (imported.RewriteFile)
            {
                await WriteFileAsync(imported, cancellationToken).ConfigureAwait(false);
            }

            await UpsertDbAsync(imported, warnOnConflict: true, cancellationToken).ConfigureAwait(false);
            await _siteProfile.SyncLanguageAsync(imported.Primary.Code, cancellationToken).ConfigureAwait(false);
            return;
        }

        var stored = await FindRecordAsync(cancellationToken).ConfigureAwait(false);
        var state = stored is null
            ? DefaultProjection()
            : await ExportRecordAsync(stored, cancellationToken).ConfigureAwait(false);
        await CommitAsync(state, syncLanguage: true, warnOnConflict: stored is not null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存、同步或构建前的闸门。文件不存在不算拒绝。</summary>
    public async Task<WorkspaceWriteResult?> PeekSaveRefusalAsync(CancellationToken cancellationToken = default)
    {
        var format = await WorkspaceFormatGate.TryRefuseAsync(_layout.Workspace.SiteSettingsFile, cancellationToken).ConfigureAwait(false);
        if (format is not null)
        {
            return format;
        }

        var document = await LocalizationYaml.ReadAsync(_layout.Workspace.LocalizationFile, cancellationToken).ConfigureAwait(false);
        return document.ErrorCode is null
            ? null
            : WorkspaceWriteResult.Fail(document.ErrorCode, document.ErrorMessage ?? document.ErrorCode);
    }

    /// <summary>生成构建时使用的本地化快照。文件不可用时抛出，避免用旧投影继续构建。</summary>
    public async Task<BuildLocalizationOptions> GetBuildLocalizationOptionsAsync(CancellationToken cancellationToken = default)
    {
        var refusal = await PeekSaveRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            throw new WorkspaceSnapshotException(refusal.Message ?? refusal.Code ?? WorkspaceWriteResult.LocalizationInvalidCode);
        }

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

    /// <summary>按主键读取投影；没有记录时返回 null，不在这里插入默认行。</summary>
    private async Task<LocalizationSettingsRecord?> FindRecordAsync(CancellationToken cancellationToken)
        => await _db.LocalizationSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>从已有投影读出 Common 覆盖；没有记录时是空列表，保存语言时不会把覆盖清掉。</summary>
    private static IReadOnlyList<CommonI18nTextOverride> ReadStoredOverrides(LocalizationSettingsRecord? record)
        => record is null ? [] : DeserializeCommonTextOverrides(record.CommonTextOverridesJson);

    /// <summary>当前投影。没有数据库行时用 zh-CN 默认，供只改 Common 文案的保存补齐文件。</summary>
    private async Task<LocalizationProjection> ReadProjectionAsync(CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(cancellationToken).ConfigureAwait(false);
        return record is null ? DefaultProjection() : ProjectionFromRecord(record);
    }

    /// <summary>最小默认：只启用 zh-CN，URL 策略固定为 PrimaryUnprefixed。</summary>
    private static LocalizationProjection DefaultProjection()
    {
        var primary = BuiltInLanguages.First(x => SameCode(x.Code, DefaultPrimaryLanguage));
        return new LocalizationProjection(primary, [primary], [], [], PrimaryUnprefixedUrlPolicy, RewriteFile: false);
    }

    /// <summary>把数据库行收成投影，不改主语言。启动导出要另外套上 site.yaml 的 language。</summary>
    private static LocalizationProjection ProjectionFromRecord(LocalizationSettingsRecord record)
    {
        var custom = NormalizeCustomLanguages(DeserializeLanguages(record.CustomLanguagesJson));
        var primary = ResolveLanguage(record.PrimaryLanguage, custom);
        var enabled = DeserializeLanguages(record.EnabledLanguagesJson)
            .Select(language => ResolveLanguage(language.Code, custom))
            .DistinctBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rewrite = false;
        if (!enabled.Any(language => SameCode(language.Code, primary.Code)))
        {
            enabled.Add(primary);
            rewrite = true;
        }

        if (enabled.Count == 0)
        {
            enabled.Add(primary);
            rewrite = true;
        }

        return new LocalizationProjection(
            primary,
            enabled,
            custom,
            DeserializeCommonTextOverrides(record.CommonTextOverridesJson),
            string.IsNullOrWhiteSpace(record.UrlPolicy) ? PrimaryUnprefixedUrlPolicy : record.UrlPolicy,
            rewrite);
    }

    /// <summary>无文件时的导出。primaryLanguage 优先取 site.yaml 的 language，没有再用数据库。</summary>
    private async Task<LocalizationProjection> ExportRecordAsync(LocalizationSettingsRecord record, CancellationToken cancellationToken)
    {
        var siteLanguage = await _siteProfile.TryReadWorkspaceLanguageAsync(cancellationToken).ConfigureAwait(false);
        var current = ProjectionFromRecord(record);
        if (string.IsNullOrWhiteSpace(siteLanguage) || SameCode(siteLanguage, current.Primary.Code))
        {
            return current with { RewriteFile = true };
        }

        var primary = ResolveLanguage(siteLanguage, current.Custom);
        var enabled = current.Enabled.ToList();
        if (!enabled.Any(language => SameCode(language.Code, primary.Code)))
        {
            enabled.Add(primary);
        }

        return current with { Primary = primary, Enabled = enabled, RewriteFile = true };
    }

    /// <summary>把文件收成可写入数据库的投影。primary 不在 enabled 里时补上，并要求写回文件。</summary>
    private static LocalizationProjection NormalizeImported(LocalizationYamlDocument document)
    {
        var custom = NormalizeCustomLanguages(document.CustomLanguages);
        var primary = ResolveLanguage(document.PrimaryLanguage, custom);
        var enabled = document.EnabledLanguages
            .Select(language => ResolveLanguage(language.Code, custom))
            .DistinctBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rewrite = custom.Count != document.CustomLanguages.Count;
        if (!enabled.Any(language => SameCode(language.Code, primary.Code)))
        {
            enabled.Add(primary);
            rewrite = true;
        }

        return new LocalizationProjection(
            primary,
            enabled,
            custom,
            NormalizeCommonTextOverrides(document.CommonTextOverrides),
            string.IsNullOrWhiteSpace(document.UrlPolicy) ? PrimaryUnprefixedUrlPolicy : document.UrlPolicy!,
            rewrite);
    }

    /// <summary>后台保存的规范化：主语言一定在启用列表里，不认识的主语言会变成自定义语言。</summary>
    private static LocalizationProjection NormalizeForSave(
        string primaryLanguageCode,
        IEnumerable<string> enabledLanguageCodes,
        IEnumerable<LanguageRecord> customLanguages,
        IReadOnlyList<CommonI18nTextOverride> overrides)
    {
        var custom = NormalizeCustomLanguages(customLanguages);
        var primary = ResolveLanguage(primaryLanguageCode, custom);
        var enabled = enabledLanguageCodes
            .Select(code => ResolveLanguage(code, custom))
            .DistinctBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!MergeAvailableLanguages(custom).Any(language => SameCode(language.Code, primary.Code)))
        {
            custom = [.. custom, primary];
        }

        if (!enabled.Any(language => SameCode(language.Code, primary.Code)))
        {
            enabled.Add(primary);
        }

        if (enabled.Count == 0)
        {
            enabled.Add(primary);
        }

        return new LocalizationProjection(primary, enabled, custom, overrides, PrimaryUnprefixedUrlPolicy, RewriteFile: true);
    }

    /// <summary>先写 YAML，再写数据库。需要时再同步 site.yaml 的 language。</summary>
    private async Task<WorkspaceWriteResult> CommitAsync(
        LocalizationProjection state,
        bool syncLanguage,
        bool warnOnConflict,
        CancellationToken cancellationToken)
    {
        await WriteFileAsync(state, cancellationToken).ConfigureAwait(false);
        await UpsertDbAsync(state, warnOnConflict, cancellationToken).ConfigureAwait(false);
        if (!syncLanguage)
        {
            return WorkspaceWriteResult.Ok;
        }

        return await _siteProfile.SyncLanguageAsync(state.Primary.Code, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把投影写成 localization.yaml。不写 UpdatedAt。</summary>
    private Task WriteFileAsync(LocalizationProjection state, CancellationToken cancellationToken)
        => LocalizationYaml.WriteAsync(
            _layout.Workspace.LocalizationFile,
            state.Primary.Code,
            state.UrlPolicy,
            state.Enabled,
            state.Custom,
            state.Overrides,
            cancellationToken);

    /// <summary>用投影覆盖数据库。内容没变时不碰 UpdatedAt，避免每次启动都写一行。</summary>
    private async Task UpsertDbAsync(LocalizationProjection state, bool warnOnConflict, CancellationToken cancellationToken)
    {
        var enabledJson = JsonSerializer.Serialize(state.Enabled, JsonOptions);
        var customJson = JsonSerializer.Serialize(state.Custom, JsonOptions);
        var overridesJson = SerializeCommonTextOverrides(state.Overrides);
        var record = await FindRecordAsync(cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            _db.LocalizationSettings.Add(new LocalizationSettingsRecord
            {
                Id = 1,
                PrimaryLanguage = state.Primary.Code,
                EnabledLanguagesJson = enabledJson,
                CustomLanguagesJson = customJson,
                CommonTextOverridesJson = overridesJson,
                UrlPolicy = state.UrlPolicy,
                UpdatedAt = _time.GetUtcNow(),
            });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var same = SameCode(record.PrimaryLanguage, state.Primary.Code)
            && string.Equals(record.UrlPolicy, state.UrlPolicy, StringComparison.Ordinal)
            && SameJson(record.EnabledLanguagesJson, enabledJson, static json => DeserializeLanguages(json))
            && SameJson(record.CustomLanguagesJson, customJson, static json => DeserializeLanguages(json))
            && string.Equals(CanonicalOverrides(record.CommonTextOverridesJson), overridesJson, StringComparison.Ordinal);
        if (same)
        {
            return;
        }

        if (warnOnConflict)
        {
            _logger.LogWarning(
                "localization.yaml 与数据库不一致，以文件为准（primary={Primary}, urlPolicy={UrlPolicy}）。",
                state.Primary.Code,
                state.UrlPolicy);
        }

        record.PrimaryLanguage = state.Primary.Code;
        record.EnabledLanguagesJson = enabledJson;
        record.CustomLanguagesJson = customJson;
        record.CommonTextOverridesJson = overridesJson;
        record.UrlPolicy = state.UrlPolicy;
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>语言 JSON 按规范化后的内容比较，不把键顺序当成冲突。</summary>
    private static bool SameJson(string left, string right, Func<string, List<LanguageRecord>> read)
        => string.Equals(JsonSerializer.Serialize(read(left), JsonOptions), JsonSerializer.Serialize(read(right), JsonOptions), StringComparison.Ordinal);

    /// <summary>Common 覆盖按保存时的稳定序列化比较。</summary>
    private static string CanonicalOverrides(string json)
        => SerializeCommonTextOverrides(DeserializeCommonTextOverrides(json));

    /// <summary>一次本地化投影。RewriteFile 只在启动导入时表示「文件需要补上 primary」。</summary>
    private sealed record LocalizationProjection(
        LanguageRecord Primary,
        IReadOnlyList<LanguageRecord> Enabled,
        IReadOnlyList<LanguageRecord> Custom,
        IReadOnlyList<CommonI18nTextOverride> Overrides,
        string UrlPolicy,
        bool RewriteFile);

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
