namespace Bocchi.HomeServer.Components.Ui;

/// <summary>
/// <see cref="BocchiI18nField"/> 在 Common 模式下编辑语言覆盖时发出的变更事件。
/// 宿主页面收到后写入自己的缓冲区，保存时一次性提交给 Localization 服务。
/// </summary>
public sealed class BocchiI18nFieldOverrideChange
{
    /// <summary>Common i18n key，例如 <c>menu.custom.about</c>。</summary>
    public required string Key { get; init; }

    /// <summary>BCP 47 风格语言代码。</summary>
    public required string LanguageCode { get; init; }

    /// <summary>用户输入的新值；空串或 null 表示清除该语言覆盖。</summary>
    public string? Value { get; init; }
}
