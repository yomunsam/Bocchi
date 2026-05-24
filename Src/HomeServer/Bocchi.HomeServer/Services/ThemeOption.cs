using Bocchi.Generator.Theme;

namespace Bocchi.HomeServer.Services;

/// <summary>Dashboard 与 Setup 可展示的前台 Theme 选项。</summary>
public sealed record ThemeOption(string Id, string Name)
{
    /// <summary>Theme 版本；旧调用方只需要 id/name 时可忽略。</summary>
    public string? Version { get; init; }

    /// <summary>Theme Contract 版本。</summary>
    public string? ContractVersion { get; init; }

    /// <summary>Theme Root 绝对路径。</summary>
    public string? Root { get; init; }

    /// <summary>Theme 来源。</summary>
    public ThemeSourceKind SourceKind { get; init; } = ThemeSourceKind.Installed;

    /// <summary>Theme runner 类型。</summary>
    public string? RunnerKind { get; init; }

    /// <summary>Theme 解析诊断。</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Dev Link 是否覆盖同 id 的 Installed Theme。</summary>
    public bool ShadowsInstalledTheme { get; init; }
}
