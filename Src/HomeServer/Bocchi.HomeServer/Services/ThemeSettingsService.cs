using System.Text.Json;
using System.Text.Json.Nodes;

using Bocchi.Generator.Theme;
using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 前台业务 Theme 配置服务。它处理 Theme Contract 配置，不处理 Dashboard 明暗外观。
/// </summary>
public sealed partial class ThemeSettingsService
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
        ["menu.about"] = CreateLanguageValues("About", "关于", "關於", "紹介"),
    };

    private readonly BocchiDbContext _db;
    private readonly TimeProvider _time;
    private readonly BocchiDataLayout _layout;
    private readonly ThemeResolver _themeResolver;

    /// <summary>构造 Theme 设置服务。</summary>
    public ThemeSettingsService(BocchiDbContext db, TimeProvider time, BocchiDataLayout layout, ThemeResolver themeResolver)
    {
        ArgumentNullException.ThrowIfNull(themeResolver);
        _db = db;
        _time = time;
        _layout = layout;
        _themeResolver = themeResolver;
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
            : await LoadConfigGroupsAsync(loadedTheme.Root, configuration, cancellationToken).ConfigureAwait(false);

        return new ThemeCustomizationView
        {
            ThemeId = normalizedThemeId,
            ThemeName = loadedTheme is null ? normalizedThemeId : loadedTheme.Manifest.Name,
            Version = loadedTheme?.Version,
            ContractVersion = loadedTheme?.ContractVersion,
            ThemeRoot = loadedTheme?.Root,
            SourceKind = loadedTheme?.SourceKind,
            RunnerKind = loadedTheme is null ? null : ResolveRunnerKind(loadedTheme.Manifest),
            Diagnostics = loadedTheme?.Diagnostics ?? [],
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
        var fields = (await LoadConfigGroupsAsync(loadedTheme.Root, new JsonObject(), cancellationToken).ConfigureAwait(false))
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
}
