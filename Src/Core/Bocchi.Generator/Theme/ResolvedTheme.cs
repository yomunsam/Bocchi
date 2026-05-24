using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>已解析且可构建的 Theme。Generator 后续阶段只接收这种成功解析结果。</summary>
public sealed record ResolvedTheme
{
    /// <summary>Theme id。</summary>
    public required string Id { get; init; }

    /// <summary>Theme 展示名称。</summary>
    public required string Name { get; init; }

    /// <summary>Theme 版本。</summary>
    public required string Version { get; init; }

    /// <summary>Theme Contract 版本。</summary>
    public required string ContractVersion { get; init; }

    /// <summary>Theme Root 的绝对路径。</summary>
    public required string Root { get; init; }

    /// <summary>当前 Theme 来源。</summary>
    public required ThemeSourceKind SourceKind { get; init; }

    /// <summary>已读取的 Theme manifest。</summary>
    public required ThemeManifest Manifest { get; init; }

    /// <summary>成功解析时仍需提示的非阻断诊断。</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Dev Link 是否覆盖了同 id 的 Installed/BuiltIn Theme。</summary>
    public bool ShadowsInstalledTheme { get; init; }
}
