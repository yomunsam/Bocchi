namespace Bocchi.HomeServer.Services;

/// <summary>
/// Dashboard 与站点本地化共用的语言描述。语言图标不进入通用模型，避免把地区视觉绑定到 Home Server。
/// </summary>
public sealed class LanguageRecord
{
    /// <summary>BCP 47 风格语言代码，例如 <c>en-US</c> 或 <c>zh-CN</c>。</summary>
    public required string Code { get; init; }

    /// <summary>该语言自己的显示名称。</summary>
    public required string NativeName { get; init; }

    /// <summary>该语言的英文显示名称，便于跨语言搜索与识别。</summary>
    public required string EnglishName { get; init; }

    /// <summary>Picklist 中使用的紧凑显示文本。</summary>
    public string DisplayName
        => string.Equals(NativeName, EnglishName, StringComparison.Ordinal)
            ? $"{NativeName} ({Code})"
            : $"{NativeName} / {EnglishName} ({Code})";
}
