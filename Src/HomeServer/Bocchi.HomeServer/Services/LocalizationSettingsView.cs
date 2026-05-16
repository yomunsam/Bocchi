namespace Bocchi.HomeServer.Services;

/// <summary>
/// Settings / Localization 页面使用的只读视图模型，避免 Razor 页面直接解析数据库 JSON。
/// </summary>
public sealed class LocalizationSettingsView
{
    /// <summary>站点主要语言。</summary>
    public required LanguageRecord PrimaryLanguage { get; init; }

    /// <summary>站点启用语言；服务层保证包含主要语言。</summary>
    public required IReadOnlyList<LanguageRecord> EnabledLanguages { get; init; }

    /// <summary>用户自定义语言。</summary>
    public required IReadOnlyList<LanguageRecord> CustomLanguages { get; init; }

    /// <summary>内容本地化 URL policy。M6 固定为 <c>PrimaryUnprefixed</c>。</summary>
    public required string UrlPolicy { get; init; }
}
