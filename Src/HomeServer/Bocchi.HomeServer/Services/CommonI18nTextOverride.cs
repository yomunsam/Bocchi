namespace Bocchi.HomeServer.Services;

/// <summary>
/// Settings / Localization 中保存的 Common i18n key 覆盖。值是 plain text，不按 HTML 或 Markdown 渲染。
/// </summary>
public sealed class CommonI18nTextOverride
{
    /// <summary>Bocchi 约定或用户输入的前台 Common i18n key。</summary>
    public required string Key { get; init; }

    /// <summary>按语言代码保存的覆盖值；空字符串不会持久化。</summary>
    public required IReadOnlyDictionary<string, string> Values { get; init; }
}
