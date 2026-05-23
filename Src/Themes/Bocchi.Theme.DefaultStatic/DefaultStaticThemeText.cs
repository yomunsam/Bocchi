using System.Text.Json;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>Theme 模板可展示的一条语言记录。</summary>
internal sealed record DefaultStaticThemeLanguage
{
    /// <summary>BCP 47 风格语言代码。</summary>
    public required string Code { get; init; }

    /// <summary>该语言自己的显示名称。</summary>
    public required string NativeName { get; init; }

    /// <summary>该语言的英文显示名称。</summary>
    public required string EnglishName { get; init; }
}

/// <summary>默认 Theme 的前台文案解析器，统一处理用户覆盖、Theme 默认值和语言回退。</summary>
internal sealed class DefaultStaticThemeText
{
    /// <summary>默认 Theme 内置的 Common i18n 文案；用户覆盖和 Theme manifest 默认值始终拥有更高优先级。</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> CommonTextDefaults = CreateCommonTextDefaults();

    /// <summary>浏览器端 i18n 数据使用 camelCase，便于 JS 直接读取。</summary>
    private static readonly JsonSerializerOptions ClientJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>用户在 Settings / Localization 或 Settings / Theme 中保存的覆盖文案。</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _overrides;

    /// <summary>当前 Theme manifest 声明的私有默认文案。</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _themeDefaults;

    /// <summary>Theme manifest 声明的默认语言。</summary>
    private readonly string? _themeDefaultLanguage;

    /// <summary>当前页面语言；M6 内容变体落地前与站点主要语言一致。</summary>
    public string CurrentLanguage { get; }

    /// <summary>站点主要语言。</summary>
    public string PrimaryLanguage { get; }

    /// <summary>站点启用语言列表，提供给自定义模板使用。</summary>
    public DefaultStaticThemeLanguage[] EnabledLanguages { get; }

    /// <summary>构造前台文案解析器。</summary>
    private DefaultStaticThemeText(
        string currentLanguage,
        string primaryLanguage,
        DefaultStaticThemeLanguage[] enabledLanguages,
        Dictionary<string, Dictionary<string, string>> overrides,
        Dictionary<string, Dictionary<string, string>> themeDefaults,
        string? themeDefaultLanguage)
    {
        CurrentLanguage = currentLanguage;
        PrimaryLanguage = primaryLanguage;
        EnabledLanguages = enabledLanguages;
        _overrides = overrides;
        _themeDefaults = themeDefaults;
        _themeDefaultLanguage = themeDefaultLanguage;
    }

    /// <summary>从 Theme Context 创建默认 Theme 文案解析器。</summary>
    public static DefaultStaticThemeText From(JsonElement context)
    {
        var siteLanguage = TryGetPath(context, ["site", "language"])?.GetString() ?? "zh-CN";
        var primaryLanguage = TryGetPath(context, ["localization", "primaryLanguage"])?.GetString();
        if (string.IsNullOrWhiteSpace(primaryLanguage))
        {
            primaryLanguage = siteLanguage;
        }

        var normalizedPrimary = primaryLanguage.Trim();
        return new DefaultStaticThemeText(
            normalizedPrimary,
            normalizedPrimary,
            ReadEnabledLanguages(context, normalizedPrimary),
            ReadLanguageValueMap(TryGetPath(context, ["localization", "text"])),
            ReadThemeDefaults(context),
            TryGetPath(context, ["theme", "i18n", "defaultLanguage"])?.GetString());
    }

    /// <summary>读取某个 i18n key 的最佳文案。</summary>
    public string Get(string key)
        => Get(key, CurrentLanguage);

    /// <summary>按指定语言读取某个 i18n key 的最佳文案，用于生成浏览器端可切换的文案表。</summary>
    public string Get(string key, string? language)
    {
        foreach (var fallback in CreateLanguageFallbacks(language))
        {
            if (TryGetLocalizedValue(_overrides, key, fallback, out var overrideValue))
            {
                return overrideValue;
            }

            if (TryGetLocalizedValue(_themeDefaults, key, fallback, out var manifestValue))
            {
                return manifestValue;
            }

            if (TryGetLocalizedValue(CommonTextDefaults, key, fallback, out var commonValue))
            {
                return commonValue;
            }
        }

        return key;
    }

    /// <summary>生成浏览器端语言切换需要的 JSON；文案全部按 plain text 输出。</summary>
    public string BuildClientJson(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var text = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var key in keys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal))
        {
            text[key] = EnabledLanguages.ToDictionary(
                language => language.Code,
                language => Get(key, language.Code),
                StringComparer.OrdinalIgnoreCase);
        }

        var data = new
        {
            currentLanguage = CurrentLanguage,
            primaryLanguage = PrimaryLanguage,
            languages = EnabledLanguages,
            text,
        };
        return JsonSerializer.Serialize(data, ClientJsonOptions);
    }

    /// <summary>创建 Bocchi 约定的 Common i18n 默认文案，供默认 Theme 在无用户覆盖时直接使用。</summary>
    private static Dictionary<string, Dictionary<string, string>> CreateCommonTextDefaults()
    {
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["menu.home"] = CreateLanguageValues("Index", "首页", "首頁", "ホーム"),
            ["menu.posts"] = CreateLanguageValues("Writing", "写作", "寫作", "文章"),
            ["menu.works"] = CreateLanguageValues("Work", "作品", "作品", "制作"),
            ["menu.notes"] = CreateLanguageValues("Notes", "札记", "札記", "ノート"),
            ["menu.friends"] = CreateLanguageValues("Friends", "友链", "友站", "リンク"),
            ["menu.about"] = CreateLanguageValues("About", "关于", "關於", "紹介"),
            ["common.readMore"] = CreateLanguageValues("Read more", "继续阅读", "繼續閱讀", "続きを読む"),
            ["common.backHome"] = CreateLanguageValues("Back to index", "返回首页", "返回首頁", "ホームへ戻る"),
            ["common.previous"] = CreateLanguageValues("Previous", "上一篇", "上一篇", "前へ"),
            ["common.next"] = CreateLanguageValues("Next", "下一篇", "下一篇", "次へ"),
            ["content.translationNotice"] = CreateLanguageValues("This page is available in another language.", "此页面有其他语言版本。", "此頁面有其他語言版本。", "このページには他の言語版があります。"),
            ["content.viewOriginal"] = CreateLanguageValues("View original", "查看原文", "查看原文", "原文を見る"),
        };
    }

    /// <summary>创建一组默认 Theme 首批支持语言的文案值。</summary>
    private static Dictionary<string, string> CreateLanguageValues(string enUs, string zhCn, string zhTw, string jaJp)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = enUs,
            ["zh-CN"] = zhCn,
            ["zh-TW"] = zhTw,
            ["ja-JP"] = jaJp,
        };
    }

    /// <summary>创建语言回退顺序：当前页面语言、站点主要语言、Theme 默认语言。</summary>
    private List<string> CreateLanguageFallbacks(string? language)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(language);
        Add(PrimaryLanguage);
        Add(_themeDefaultLanguage);
        return result;

        void Add(string? language)
        {
            if (!string.IsNullOrWhiteSpace(language) && seen.Add(language.Trim()))
            {
                result.Add(language.Trim());
            }
        }
    }

    /// <summary>读取 Theme Context 中的启用语言；缺失时至少返回站点主要语言。</summary>
    private static DefaultStaticThemeLanguage[] ReadEnabledLanguages(JsonElement context, string primaryLanguage)
    {
        var result = new List<DefaultStaticThemeLanguage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var languages = TryGetPath(context, ["localization", "enabledLanguages"]);
        if (languages is { ValueKind: JsonValueKind.Array })
        {
            foreach (var language in languages.Value.EnumerateArray())
            {
                var code = GetString(language, "code");
                if (string.IsNullOrWhiteSpace(code) || !seen.Add(code.Trim()))
                {
                    continue;
                }

                result.Add(new DefaultStaticThemeLanguage
                {
                    Code = code.Trim(),
                    NativeName = GetString(language, "nativeName", code).Trim(),
                    EnglishName = GetString(language, "englishName", code).Trim(),
                });
            }
        }

        if (!seen.Contains(primaryLanguage))
        {
            result.Insert(0, new DefaultStaticThemeLanguage
            {
                Code = primaryLanguage,
                NativeName = primaryLanguage,
                EnglishName = primaryLanguage,
            });
        }

        return result.ToArray();
    }

    /// <summary>读取 Theme manifest 中声明的私有默认文案。</summary>
    private static Dictionary<string, Dictionary<string, string>> ReadThemeDefaults(JsonElement context)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var keys = TryGetPath(context, ["theme", "i18n", "keys"]);
        if (keys is not { ValueKind: JsonValueKind.Array })
        {
            return result;
        }

        foreach (var item in keys.Value.EnumerateArray())
        {
            var key = GetString(item, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var values = ReadLanguageValues(item.TryGetProperty("defaultValues", out var defaults) ? defaults : null);
            if (values.Count > 0)
            {
                result[key.Trim()] = values;
            }
        }

        return result;
    }

    /// <summary>读取 key -> language -> text 形态的覆盖文案。</summary>
    private static Dictionary<string, Dictionary<string, string>> ReadLanguageValueMap(JsonElement? text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (text is not { ValueKind: JsonValueKind.Object })
        {
            return result;
        }

        foreach (var property in text.Value.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }

            var values = ReadLanguageValues(property.Value);
            if (values.Count > 0)
            {
                result[property.Name.Trim()] = values;
            }
        }

        return result;
    }

    /// <summary>读取 language -> text 形态的 plain text 文案对象。</summary>
    private static Dictionary<string, string> ReadLanguageValues(JsonElement? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is not { ValueKind: JsonValueKind.Object })
        {
            return result;
        }

        foreach (var property in values.Value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(property.Name) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[property.Name.Trim()] = value.Trim();
        }

        return result;
    }

    /// <summary>按 key 与语言从文案表中读取值。</summary>
    private static bool TryGetLocalizedValue(
        Dictionary<string, Dictionary<string, string>> source,
        string key,
        string language,
        out string value)
    {
        value = string.Empty;
        if (!source.TryGetValue(key, out var values) || !values.TryGetValue(language, out var found) || found is null)
        {
            return false;
        }

        value = found;
        return true;
    }

    /// <summary>读取字符串属性。</summary>
    private static string GetString(JsonElement item, string key, string fallback = "")
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    /// <summary>按路径读取嵌套 JSON 值。</summary>
    private static JsonElement? TryGetPath(JsonElement root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current;
    }
}
