namespace Bocchi.HomeServer.Services;

/// <summary>Dashboard 使用的当前 Theme 页面合同视图。</summary>
public sealed class ThemePageContractView
{
    /// <summary>Theme id。</summary>
    public required string ThemeId { get; init; }

    /// <summary>Theme 展示名。</summary>
    public required string ThemeName { get; init; }

    /// <summary>Theme 接受的 Page 模板；至少包含 normal。</summary>
    public required IReadOnlyList<ThemePageTemplateOption> PageTemplates { get; init; }

    /// <summary>Theme 自己提供的特殊页面。</summary>
    public required IReadOnlyList<ThemeSpecialPageOption> SpecialPages { get; init; }
}

/// <summary>Dashboard 下拉框中的 Page 模板选项。</summary>
public sealed class ThemePageTemplateOption
{
    /// <summary>模板名称，也是 Page frontmatter 中持久化的值。</summary>
    public required string Name { get; init; }

    /// <summary>按当前 Dashboard UI language 解析后的展示名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>Theme manifest 中的原始 displayName，可能是 i18n display ref。</summary>
    public required string RawDisplayName { get; init; }
}

/// <summary>Dashboard Menu target 中的 Theme special page 选项。</summary>
public sealed class ThemeSpecialPageOption
{
    /// <summary>特殊页面名称，也是 Menu target 持久化值。</summary>
    public required string Name { get; init; }

    /// <summary>按当前 Dashboard UI language 解析后的展示名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>Theme manifest 中的原始 displayName，可能是 i18n display ref。</summary>
    public required string RawDisplayName { get; init; }

    /// <summary>站点根相对路径。</summary>
    public required string Route { get; init; }
}
