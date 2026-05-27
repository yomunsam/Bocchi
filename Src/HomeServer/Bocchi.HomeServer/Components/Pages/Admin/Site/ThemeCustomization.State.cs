using Bocchi.Generator.Theme;
using Bocchi.GeneratorContract;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.Components;

namespace Bocchi.HomeServer.Components.Pages.Admin.Site;

/// <summary>ThemeCustomization 页面状态和编辑动作；标记文件只保留路由、注入和 UI markup。</summary>
public partial class ThemeCustomization
{
    /// <summary>配置项 tab 的内部标识，只影响 Dashboard UI 状态。</summary>
    private const string ConfigTab = "config";

    /// <summary>Theme 私有文本 tab 的内部标识，只影响 Dashboard UI 状态。</summary>
    private const string TextTab = "text";

    /// <summary>当前站点正在使用的前台 Theme id，来自站点基础约定。</summary>
    private string _activeThemeId = "default-static";

    /// <summary>Theme schema 定制页视图。</summary>
    private ThemeCustomizationView? _customization;

    /// <summary>当前打开的任务 tab。</summary>
    private string _activeTab = ConfigTab;

    /// <summary>当前配置分组 id；分组来自 Theme schema。</summary>
    private string? _activeConfigGroupId;

    /// <summary>当前选中的 Theme 私有 i18n key。</summary>
    private string? _selectedThemeI18nKey;

    /// <summary>普通单值字段编辑缓冲区，key 与 Theme schema 字段 key 对齐。</summary>
    private Dictionary<string, string> _textValues = new(StringComparer.Ordinal);

    /// <summary>Boolean 字段编辑缓冲区。</summary>
    private Dictionary<string, bool> _booleanValues = new(StringComparer.Ordinal);

    /// <summary>MultiSelect 字段编辑缓冲区。</summary>
    private Dictionary<string, HashSet<string>> _multiSelectValues = new(StringComparer.Ordinal);

    /// <summary>LocalizedText 字段编辑缓冲区，字段 key -> 语言代码 -> 用户覆盖文案。</summary>
    private Dictionary<string, Dictionary<string, string>> _localizedTextValues = new(StringComparer.Ordinal);

    /// <summary>LocalizedTextList 字段编辑缓冲区，字段 key -> 语言代码 -> 用户覆盖列表。</summary>
    private Dictionary<string, Dictionary<string, List<string>>> _localizedTextListValues = new(StringComparer.Ordinal);

    /// <summary>LocalizedTextList 给 BocchiLocalizedInput 的字符串视图缓冲区，按换行 join；保留对象引用以避免组件重置内部状态。</summary>
    private Dictionary<string, Dictionary<string, string>> _localizedTextListBuffer = new(StringComparer.Ordinal);

    /// <summary>站点启用语言集合，用于 Theme 私有文本输入。</summary>
    private HashSet<string> _enabledLanguageCodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前启用语言按可展示顺序的扁平 code 列表，喂给 BocchiLocalizedInput。</summary>
    private IReadOnlyList<string> _enabledLanguageCodeList = Array.Empty<string>();

    /// <summary>站点主语言代码；缺失时回退到默认值。</summary>
    private string _primaryLanguageCode = LocalizationSettingsService.DefaultPrimaryLanguage;

    /// <summary>可展示语言列表，内置语言在前，自定义语言在后。</summary>
    private IReadOnlyList<LanguageRecord> _availableLanguages = LocalizationSettingsService.BuiltInLanguages;

    /// <summary>Theme 私有 i18n key 搜索词。</summary>
    private string _themeI18nSearch = string.Empty;

    /// <summary>当前 Theme manifest 声明的私有 i18n key。</summary>
    private List<ThemeI18nKeyView> _themeI18nKeys = [];

    /// <summary>Theme 私有 i18n 覆盖编辑缓冲区，形态为 key -> language -> plain text。</summary>
    private Dictionary<string, Dictionary<string, string>> _themeI18nTextOverrides = new(StringComparer.Ordinal);

    /// <summary>全页保存中标记；配置和私有文本共用一个保存入口。</summary>
    private bool _saveBusy;

    /// <summary>配置项是否有未保存改动。</summary>
    private bool _configDirty;

    /// <summary>Theme 私有文本是否有未保存改动。</summary>
    private bool _themeI18nDirty;

    /// <summary>Theme 配置保存反馈状态。</summary>
    private bool _configSaved;

    /// <summary>Theme 配置保存反馈文案。</summary>
    private string? _configMessage;

    /// <summary>Theme 私有 i18n 覆盖保存反馈。</summary>
    private bool _themeI18nSaved;

    /// <summary>Theme 私有 i18n 覆盖保存后的说明文本。</summary>
    private string? _themeI18nMessage;

    protected override async Task OnInitializedAsync()
    {
        var site = await SiteProfileSettings.GetAsync();
        _activeThemeId = string.IsNullOrWhiteSpace(site.DefaultThemeId) ? "default-static" : site.DefaultThemeId;
        await LoadLocalizationAsync();
        await LoadCustomizationAsync();
        await LoadThemeI18nAsync();
    }

    /// <summary>顶部保存按钮是否可用；没有脏数据时保持禁用，避免用户误判保存范围。</summary>
    private bool CanSaveChanges => !_saveBusy && (_configDirty || _themeI18nDirty);

    /// <summary>顶部保存按钮文案。</summary>
    private string SaveButtonText
        => _saveBusy ? I18n["themeCustomization.actions.savingChanges"] : I18n["themeCustomization.actions.saveChanges"];

    /// <summary>配置项状态标签文案。</summary>
    private string ConfigStatusText
        => _configMessage is not null && !_configSaved
            ? _configMessage
            : _configDirty
            ? I18n["themeCustomization.status.configDirty"]
            : _configSaved
                ? _configMessage ?? I18n["themeCustomization.status.configSaved"]
                : I18n["themeCustomization.status.configIdle"];

    /// <summary>配置项状态标签语义色。</summary>
    private string ConfigStatusTone
        => _configMessage is not null && !_configSaved
            ? "danger"
            : _configDirty ? "warning" : _configSaved ? "success" : "neutral";

    /// <summary>Theme 私有文本状态标签文案。</summary>
    private string ThemeI18nStatusText
        => _themeI18nMessage is not null && !_themeI18nSaved
            ? _themeI18nMessage
            : _themeI18nDirty
            ? I18n["themeCustomization.status.textDirty"]
            : _themeI18nSaved
                ? _themeI18nMessage ?? I18n["themeCustomization.status.textSaved"]
                : I18n["themeCustomization.status.textIdle"];

    /// <summary>Theme 私有文本状态标签语义色。</summary>
    private string ThemeI18nStatusTone
        => _themeI18nMessage is not null && !_themeI18nSaved
            ? "danger"
            : _themeI18nDirty ? "warning" : _themeI18nSaved ? "success" : "neutral";

    /// <summary>把 Theme 来源枚举转换为 Dashboard 当前语言文案。</summary>
    private string ThemeSourceLabel(ThemeSourceKind? sourceKind)
        => sourceKind switch
        {
            ThemeSourceKind.BuiltIn => I18n["themeCustomization.source.builtIn"],
            ThemeSourceKind.Installed => I18n["themeCustomization.source.installed"],
            ThemeSourceKind.DevLink => I18n["themeCustomization.source.devLink"],
            ThemeSourceKind.PackageCandidate => I18n["themeCustomization.source.packageCandidate"],
            _ => I18n["themeCustomization.meta.unknown"],
        };

    /// <summary>把 Resolver 诊断级别映射到现有状态标签色值。</summary>
    private static string ThemeDiagnosticTone(ThemeDiagnostic diagnostic)
        => diagnostic.Severity switch
        {
            ThemeDiagnosticSeverity.Error => "danger",
            ThemeDiagnosticSeverity.Warning => "warning",
            _ => "neutral",
        };

    /// <summary>Dashboard 使用资源文件展示诊断，避免把服务层日志文案直接暴露成 UI 文案。</summary>
    private string ThemeDiagnosticText(ThemeDiagnostic diagnostic)
    {
        var key = $"themeDiagnostics.{diagnostic.Code}";
        var text = I18n[key];
        return string.Equals(text, key, StringComparison.Ordinal)
            ? diagnostic.Code
            : text;
    }

    /// <summary>当前配置分组；schema 更新后如果 id 不存在会回退到第一个分组。</summary>
    private ThemeConfigGroupView? ActiveConfigGroup
        => _customization is null
            ? null
            : _customization.Groups.FirstOrDefault(group => string.Equals(group.Id, _activeConfigGroupId, StringComparison.Ordinal))
              ?? (_customization.Groups.Count > 0 ? _customization.Groups[0] : null);

    /// <summary>当前选中的 Theme 私有文本 key。</summary>
    private ThemeI18nKeyView? SelectedThemeI18nKey
        => _themeI18nKeys.FirstOrDefault(key => string.Equals(key.Key, _selectedThemeI18nKey, StringComparison.Ordinal));

    /// <summary>当前已启用语言的展示列表，按 Picklist 顺序显示。</summary>
    private List<LanguageRecord> EnabledLanguagesForDisplay
        => _availableLanguages
            .Where(language => _enabledLanguageCodes.Contains(language.Code))
            .ToList();

    /// <summary>当前可编辑的 Theme 私有 i18n key；仅来自 Theme manifest 声明。</summary>
    private List<ThemeI18nKeyView> FilteredThemeI18nKeys
    {
        get
        {
            var query = _themeI18nSearch.Trim();
            return _themeI18nKeys
                .Where(key => query.Length == 0
                    || key.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || key.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (key.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderBy(key => key.Key, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>读取当前 Theme schema 和配置值，并刷新页面编辑缓冲区。</summary>
    private async Task LoadCustomizationAsync()
    {
        _customization = await ThemeSettings.GetCustomizationAsync(_activeThemeId);
        _textValues = new Dictionary<string, string>(StringComparer.Ordinal);
        _booleanValues = new Dictionary<string, bool>(StringComparer.Ordinal);
        _multiSelectValues = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        _localizedTextValues = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        _localizedTextListValues = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        _localizedTextListBuffer = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var field in _customization.Groups.SelectMany(group => group.Fields))
        {
            if (field.Type == ThemeConfigFieldType.Boolean)
            {
                _booleanValues[field.Key] = field.BooleanValue;
            }
            else if (field.Type == ThemeConfigFieldType.MultiSelect)
            {
                _multiSelectValues[field.Key] = new HashSet<string>(field.SelectedValues, StringComparer.Ordinal);
            }
            else if (field.Type == ThemeConfigFieldType.LocalizedText)
            {
                _localizedTextValues[field.Key] = field.LocalizedTextValues.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
            }
            else if (field.Type == ThemeConfigFieldType.LocalizedTextList)
            {
                _localizedTextListValues[field.Key] = field.LocalizedTextListValues.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase);
                _localizedTextListBuffer[field.Key] = _localizedTextListValues[field.Key].ToDictionary(
                    pair => pair.Key,
                    pair => string.Join(Environment.NewLine, pair.Value),
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _textValues[field.Key] = field.TextValue;
            }
        }

        EnsureActiveConfigGroup();
        _configDirty = false;
    }

    /// <summary>读取站点启用语言，Theme 私有文本只按这些语言展示输入框。</summary>
    private async Task LoadLocalizationAsync()
    {
        var localization = await LocalizationSettings.GetAsync();
        _enabledLanguageCodes = new HashSet<string>(
            localization.EnabledLanguages.Select(language => language.Code),
            StringComparer.OrdinalIgnoreCase);
        _availableLanguages = [.. LocalizationSettingsService.BuiltInLanguages, .. localization.CustomLanguages];
        _enabledLanguageCodeList = localization.EnabledLanguages.Select(language => language.Code).ToArray();
        _primaryLanguageCode = localization.PrimaryLanguage.Code;
    }

    /// <summary>读取 Theme manifest 声明和用户填写的 Theme 私有 i18n 覆盖。</summary>
    private async Task LoadThemeI18nAsync()
    {
        var themeI18n = await ThemeSettings.GetI18nAsync(_activeThemeId);
        _themeI18nKeys = [.. themeI18n.Keys];
        _themeI18nTextOverrides = themeI18n.TextOverrides.ToDictionary(
            x => x.Key,
            x => new Dictionary<string, string>(x.Values, StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);
        EnsureSelectedThemeI18nKey();
        _themeI18nDirty = false;
    }

    /// <summary>保存所有有改动的区域；配置与 Theme 私有文本仍分别走既有服务方法。</summary>
    private async Task SaveChangesAsync()
    {
        if (!CanSaveChanges)
        {
            return;
        }

        _saveBusy = true;
        try
        {
            if (_configDirty)
            {
                await SaveConfigChangesAsync();
            }

            if (_themeI18nDirty)
            {
                await SaveThemeI18nChangesAsync();
            }
        }
        finally
        {
            _saveBusy = false;
        }
    }

    /// <summary>保存 schema 字段值；服务层负责按字段类型写回 nested JSON。</summary>
    private async Task SaveConfigChangesAsync()
    {
        if (_customization is null)
        {
            return;
        }

        try
        {
            var values = _customization.Groups
                .SelectMany(group => group.Fields)
                .Where(field => field.Type != ThemeConfigFieldType.Group)
                .Select(BuildValueInput)
                .ToList();
            await ThemeSettings.SaveCustomizationAsync(_activeThemeId, values);
            await LoadCustomizationAsync();
            _configSaved = true;
            _configMessage = I18n["themeCustomization.config.saved"];
        }
        catch (InvalidOperationException ex)
        {
            _configSaved = false;
            _configMessage = ex.Message;
        }
    }

    /// <summary>保存当前 Theme 私有 i18n 覆盖；空输入会在服务层被丢弃。</summary>
    private async Task SaveThemeI18nChangesAsync()
    {
        try
        {
            var overrides = _themeI18nTextOverrides.Select(x => new ThemeI18nTextOverride
            {
                Key = x.Key,
                Values = x.Value,
            });
            await ThemeSettings.SaveI18nTextOverridesAsync(_activeThemeId, overrides);
            await LoadThemeI18nAsync();
            _themeI18nSaved = true;
            _themeI18nMessage = I18n["settings.theme.i18nSaved"];
        }
        catch (InvalidOperationException ex)
        {
            _themeI18nSaved = false;
            _themeI18nMessage = ex.Message;
        }
    }

    /// <summary>把页面缓冲区中的字段值转换成服务层提交模型。</summary>
    private ThemeConfigValueInput BuildValueInput(ThemeConfigFieldView field)
        => field.Type switch
        {
            ThemeConfigFieldType.Boolean => new ThemeConfigValueInput
            {
                Key = field.Key,
                Value = GetBooleanValue(field.Key).ToString(),
            },
            ThemeConfigFieldType.MultiSelect => new ThemeConfigValueInput
            {
                Key = field.Key,
                Values = _multiSelectValues.TryGetValue(field.Key, out var values) ? values.ToList() : [],
            },
            ThemeConfigFieldType.LocalizedText => new ThemeConfigValueInput
            {
                Key = field.Key,
                LocalizedValues = _localizedTextValues.TryGetValue(field.Key, out var values)
                    ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            },
            ThemeConfigFieldType.LocalizedTextList => new ThemeConfigValueInput
            {
                Key = field.Key,
                LocalizedListValues = _localizedTextListValues.TryGetValue(field.Key, out var values)
                    ? values.ToDictionary(
                        pair => pair.Key,
                        pair => (IReadOnlyList<string>)pair.Value.ToList(),
                        StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            },
            _ => new ThemeConfigValueInput
            {
                Key = field.Key,
                Value = GetTextValue(field.Key),
            },
        };

    /// <summary>切换当前任务 tab。</summary>
    private void SetActiveTab(string tab)
    {
        _activeTab = tab == TextTab ? TextTab : ConfigTab;
    }

    /// <summary>判断 tab 是否为当前打开项。</summary>
    private bool IsActiveTab(string tab) => string.Equals(_activeTab, tab, StringComparison.Ordinal);

    /// <summary>生成 tab 按钮 class。</summary>
    private string TabButtonClass(string tab)
        => IsActiveTab(tab) ? "bocchi-theme-tab bocchi-theme-tab--active" : "bocchi-theme-tab";

    /// <summary>显式输出 ARIA boolean，避免布尔属性被渲染成空值。</summary>
    private static string AriaBool(bool value) => value ? "true" : "false";

    /// <summary>切换配置分组。</summary>
    private void SelectConfigGroup(string groupId)
    {
        _activeConfigGroupId = groupId;
    }

    /// <summary>判断配置分组是否正在编辑。</summary>
    private bool IsActiveConfigGroup(ThemeConfigGroupView group)
        => string.Equals(ActiveConfigGroup?.Id, group.Id, StringComparison.Ordinal);

    /// <summary>生成配置分组按钮 class。</summary>
    private string ConfigGroupButtonClass(ThemeConfigGroupView group)
        => IsActiveConfigGroup(group) ? "bocchi-theme-side-button bocchi-theme-side-button--active" : "bocchi-theme-side-button";

    /// <summary>确保配置分组选择始终落在 schema 声明范围内。</summary>
    private void EnsureActiveConfigGroup()
    {
        if (_customization?.Groups.Count is not > 0)
        {
            _activeConfigGroupId = null;
            return;
        }

        if (_activeConfigGroupId is not null &&
            _customization.Groups.Any(group => string.Equals(group.Id, _activeConfigGroupId, StringComparison.Ordinal)))
        {
            return;
        }

        _activeConfigGroupId = _customization.Groups[0].Id;
    }

    /// <summary>切换 Theme 私有文本 key。</summary>
    private void SelectThemeI18nKey(string key)
    {
        _selectedThemeI18nKey = key;
    }

    /// <summary>判断 Theme 私有文本 key 是否被选中。</summary>
    private bool IsSelectedThemeI18nKey(ThemeI18nKeyView key)
        => string.Equals(_selectedThemeI18nKey, key.Key, StringComparison.Ordinal);

    /// <summary>生成 Theme 私有文本 key 按钮 class。</summary>
    private string ThemeI18nKeyButtonClass(ThemeI18nKeyView key)
        => IsSelectedThemeI18nKey(key) ? "bocchi-theme-key-button bocchi-theme-key-button--active" : "bocchi-theme-key-button";

    /// <summary>更新 Theme 私有文本搜索词，并把编辑面板同步到搜索结果中的第一个 key。</summary>
    private void SetThemeI18nSearch(string? value)
    {
        _themeI18nSearch = value ?? string.Empty;
        var filtered = FilteredThemeI18nKeys;
        if (filtered.Count == 0)
        {
            return;
        }

        if (_selectedThemeI18nKey is null ||
            !filtered.Any(key => string.Equals(key.Key, _selectedThemeI18nKey, StringComparison.Ordinal)))
        {
            _selectedThemeI18nKey = filtered[0].Key;
        }
    }

    /// <summary>确保 Theme 私有文本初次进入页面时有一个明确的编辑对象。</summary>
    private void EnsureSelectedThemeI18nKey()
    {
        if (_themeI18nKeys.Count == 0)
        {
            _selectedThemeI18nKey = null;
            return;
        }

        if (_selectedThemeI18nKey is not null &&
            _themeI18nKeys.Any(key => string.Equals(key.Key, _selectedThemeI18nKey, StringComparison.Ordinal)))
        {
            return;
        }

        _selectedThemeI18nKey = _themeI18nKeys
            .OrderBy(key => key.Key, StringComparer.Ordinal)
            .First()
            .Key;
    }

    /// <summary>读取单值字段编辑值。</summary>
    private string GetTextValue(string key)
        => _textValues.TryGetValue(key, out var value) ? value : string.Empty;

    /// <summary>更新单值字段编辑缓冲区。</summary>
    private void SetTextValue(string key, string? value)
    {
        _textValues[key] = value ?? string.Empty;
        _configDirty = true;
        _configSaved = false;
        _configMessage = null;
    }

    /// <summary>读取 Boolean 字段编辑值。</summary>
    private bool GetBooleanValue(string key)
        => _booleanValues.TryGetValue(key, out var value) && value;

    /// <summary>更新 Boolean 字段编辑缓冲区。</summary>
    private void SetBooleanValue(string key, ChangeEventArgs args)
    {
        _booleanValues[key] = args.Value is bool value && value;
        _configDirty = true;
        _configSaved = false;
        _configMessage = null;
    }

    /// <summary>判断 MultiSelect 字段是否包含某个选项。</summary>
    private bool IsMultiSelectValueSelected(string key, string value)
        => _multiSelectValues.TryGetValue(key, out var values) && values.Contains(value);

    /// <summary>切换 MultiSelect 字段中的某个选项。</summary>
    private void ToggleMultiSelectValue(string key, string value, ChangeEventArgs args)
    {
        if (!_multiSelectValues.TryGetValue(key, out var values))
        {
            values = new HashSet<string>(StringComparer.Ordinal);
            _multiSelectValues[key] = values;
        }

        if (args.Value is bool selected && selected)
        {
            values.Add(value);
        }
        else
        {
            values.Remove(value);
        }

        _configDirty = true;
        _configSaved = false;
        _configMessage = null;
    }

    /// <summary>读取 LocalizedText 字段在某个语言下的用户覆盖文案。</summary>
    private string GetLocalizedTextValue(string key, string languageCode)
        => _localizedTextValues.TryGetValue(key, out var values)
            && values.TryGetValue(languageCode, out var value)
            ? value
            : string.Empty;

    /// <summary>更新 LocalizedText 字段；空输入会删除该语言覆盖，让 Theme 默认值接管。</summary>
    private void SetLocalizedTextValue(string key, string languageCode, string? value)
    {
        if (!_localizedTextValues.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _localizedTextValues[key] = values;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            values.Remove(languageCode);
        }
        else
        {
            values[languageCode] = value.Trim();
        }

        MarkConfigDirty();
    }

    /// <summary>读取 LocalizedTextList 字段在某个语言下的用户覆盖列表，按一行一项展示。</summary>
    private string GetLocalizedTextListValue(string key, string languageCode)
        => _localizedTextListValues.TryGetValue(key, out var values)
            && values.TryGetValue(languageCode, out var value)
            ? string.Join(Environment.NewLine, value)
            : string.Empty;

    /// <summary>更新 LocalizedTextList 字段；空输入会删除该语言覆盖，让 Theme 默认列表接管。</summary>
    private void SetLocalizedTextListValue(string key, string languageCode, string? value)
    {
        if (!_localizedTextListValues.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _localizedTextListValues[key] = values;
        }

        var list = SplitLocalizedListInput(value);
        if (list.Count == 0)
        {
            values.Remove(languageCode);
        }
        else
        {
            values[languageCode] = list;
        }

        MarkConfigDirty();
    }

    /// <summary>读取 LocalizedText 字段在某个语言下的 schema 默认文案。</summary>
    private static string GetDefaultLocalizedTextValue(ThemeConfigFieldView field, string languageCode)
        => field.DefaultLocalizedTextValues.TryGetValue(languageCode, out var value) ? value : string.Empty;

    /// <summary>读取 LocalizedTextList 字段在某个语言下的 schema 默认列表，按一行一项提示。</summary>
    private static string GetDefaultLocalizedTextListValue(ThemeConfigFieldView field, string languageCode)
        => field.DefaultLocalizedTextListValues.TryGetValue(languageCode, out var value)
            ? string.Join(Environment.NewLine, value)
            : string.Empty;

    /// <summary>把 Dashboard 文本域拆成列表项，保持用户填写顺序并移除空行。</summary>
    private static List<string> SplitLocalizedListInput(string? value)
        => (value ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>标记配置区有未保存改动，并清除上一次保存状态。</summary>
    private void MarkConfigDirty()
    {
        _configDirty = true;
        _configSaved = false;
        _configMessage = null;
    }

    /// <summary>读取某个 Theme 私有 key 在某个语言下的覆盖值。</summary>
    private string GetThemeI18nTextOverride(string key, string languageCode)
        => _themeI18nTextOverrides.TryGetValue(key, out var values)
            && values.TryGetValue(languageCode, out var value)
            ? value
            : string.Empty;

    /// <summary>读取 Theme manifest 默认文案，用作输入框 placeholder。</summary>
    private static string GetThemeI18nDefaultValue(ThemeI18nKeyView key, string languageCode)
        => key.DefaultValues.TryGetValue(languageCode, out var value)
            ? value
            : string.Empty;

    /// <summary>更新 Theme 私有 i18n 覆盖编辑缓冲区；空值会移除对应语言覆盖。</summary>
    private void SetThemeI18nTextOverride(string key, string languageCode, string? value)
    {
        if (!_themeI18nTextOverrides.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _themeI18nTextOverrides[key] = values;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            values.Remove(languageCode);
            if (values.Count == 0)
            {
                _themeI18nTextOverrides.Remove(key);
            }
        }
        else
        {
            values[languageCode] = value.Trim();
        }

        _themeI18nDirty = true;
        _themeI18nSaved = false;
        _themeI18nMessage = null;
    }

    /// <summary>生成字段 placeholder；优先使用 schema placeholder，其次用默认值提示。</summary>
    private static string? FieldPlaceholder(ThemeConfigFieldView field)
        => string.IsNullOrWhiteSpace(field.Placeholder) ? field.DefaultText : field.Placeholder;

    /// <summary>判断字段是否声明了默认 Theme 的受控 inline color 文本格式。</summary>
    private static bool UsesInlineColorTextFormat(ThemeConfigFieldView field)
        => string.Equals(field.TextFormat, "inlineColor", StringComparison.OrdinalIgnoreCase);

    /// <summary>把字段 key 转成稳定的 DOM id 后缀。</summary>
    private static string FieldLabelId(ThemeConfigFieldView field)
        => "theme-field-" + field.Key.Replace('.', '-').Replace(':', '-').Replace('/', '-');

    /// <summary>把色值输入收束成 HTML color input 可以接受的 <c>#rrggbb</c>。</summary>
    private static string InputColorValue(string value)
        => value.Length == 7 &&
           value[0] == '#' &&
           value.Skip(1).All(Uri.IsHexDigit)
            ? value
            : "#000000";

    /// <summary>给 BocchiLocalizedInput 喂的字段缓冲；空缺时按字段 key 懒初始化以保持引用稳定。</summary>
    private IReadOnlyDictionary<string, string> LocalizedTextValuesFor(string fieldKey)
    {
        if (!_localizedTextValues.TryGetValue(fieldKey, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _localizedTextValues[fieldKey] = values;
        }

        return values;
    }

    /// <summary>组件回填时直接替换内部字典，保持引用稳定避免下一轮重置子组件状态。</summary>
    private void OnLocalizedTextChanged(string fieldKey, IReadOnlyDictionary<string, string> next)
    {
        _localizedTextValues[fieldKey] = ToOrdinalIgnoreCase(next);
        MarkConfigDirty();
    }

    /// <summary>给 BocchiLocalizedInput 喂的列表缓冲；存的是「按换行 join 的字符串视图」。</summary>
    private IReadOnlyDictionary<string, string> LocalizedTextListValuesFor(string fieldKey)
    {
        if (!_localizedTextListBuffer.TryGetValue(fieldKey, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _localizedTextListBuffer[fieldKey] = values;
        }

        return values;
    }

    /// <summary>组件回填字符串字典；同步拆分到 List 字典作为持久化形态。</summary>
    private void OnLocalizedTextListChanged(string fieldKey, IReadOnlyDictionary<string, string> next)
    {
        var buffer = ToOrdinalIgnoreCase(next);
        _localizedTextListBuffer[fieldKey] = buffer;

        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in buffer)
        {
            var list = SplitLocalizedListInput(pair.Value);
            if (list.Count > 0)
            {
                lists[pair.Key] = list;
            }
        }

        _localizedTextListValues[fieldKey] = lists;
        MarkConfigDirty();
    }

    /// <summary>Theme 私有 i18n key 缓冲；同样懒初始化保持引用稳定。</summary>
    private IReadOnlyDictionary<string, string> ThemeI18nValuesFor(string key)
    {
        if (!_themeI18nTextOverrides.TryGetValue(key, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _themeI18nTextOverrides[key] = values;
        }

        return values;
    }

    private void OnThemeI18nValuesChanged(string key, IReadOnlyDictionary<string, string> next)
    {
        _themeI18nTextOverrides[key] = ToOrdinalIgnoreCase(next);
        _themeI18nDirty = true;
        _themeI18nSaved = false;
        _themeI18nMessage = null;
    }

    private static Dictionary<string, string> ToOrdinalIgnoreCase(IReadOnlyDictionary<string, string> source)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            dict[pair.Key] = pair.Value;
        }

        return dict;
    }
}
