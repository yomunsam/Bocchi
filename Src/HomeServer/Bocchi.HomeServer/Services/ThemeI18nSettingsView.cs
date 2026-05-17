namespace Bocchi.HomeServer.Services;

/// <summary>
/// Settings / Theme 使用的 Theme 私有 i18n 视图模型，来自当前 Theme manifest 与用户覆盖值。
/// </summary>
public sealed class ThemeI18nSettingsView
{
    /// <summary>当前 Theme id。</summary>
    public required string ThemeId { get; init; }

    /// <summary>Theme manifest 声明的支持语言。</summary>
    public required IReadOnlyList<string> SupportedLanguages { get; init; }

    /// <summary>Theme manifest 声明的默认语言。</summary>
    public string? DefaultLanguage { get; init; }

    /// <summary>Theme manifest 声明的私有 i18n key。</summary>
    public required IReadOnlyList<ThemeI18nKeyView> Keys { get; init; }

    /// <summary>用户已经填写的 Theme 私有 i18n 覆盖。</summary>
    public required IReadOnlyList<ThemeI18nTextOverride> TextOverrides { get; init; }
}

/// <summary>Theme manifest 中单个私有 i18n key 的 Dashboard 展示模型。</summary>
public sealed class ThemeI18nKeyView
{
    /// <summary>Theme 私有 key，通常带 Theme 命名空间。</summary>
    public required string Key { get; init; }

    /// <summary>Dashboard 展示标题。</summary>
    public required string Title { get; init; }

    /// <summary>Dashboard 展示说明。</summary>
    public string? Description { get; init; }

    /// <summary>Theme manifest 中声明的默认 plain text 值。</summary>
    public required IReadOnlyDictionary<string, string> DefaultValues { get; init; }
}

/// <summary>Theme 私有 i18n 覆盖；值是 plain text，不按 HTML 或 Markdown 渲染。</summary>
public sealed class ThemeI18nTextOverride
{
    /// <summary>Theme manifest 声明或用户后续保留的 Theme 私有 key。</summary>
    public required string Key { get; init; }

    /// <summary>按语言代码保存的覆盖值；空字符串不会持久化。</summary>
    public required IReadOnlyDictionary<string, string> Values { get; init; }
}
