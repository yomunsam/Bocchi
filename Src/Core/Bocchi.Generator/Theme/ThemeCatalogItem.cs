using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>Theme Catalog 中的一项。无效 Theme 也会被表示出来，便于 Dashboard 展示诊断。</summary>
public sealed record ThemeCatalogItem
{
    /// <summary>Catalog 用来引用该 Theme 的 id；无效 manifest 时来自目录名或 Dev Link id。</summary>
    public required string Id { get; init; }

    /// <summary>展示名称；manifest 不可用时回退到 <see cref="Id"/>。</summary>
    public required string Name { get; init; }

    /// <summary>Theme 版本；manifest 不可用时为空。</summary>
    public string? Version { get; init; }

    /// <summary>Theme Contract 版本；manifest 不可用时为空。</summary>
    public string? ContractVersion { get; init; }

    /// <summary>Theme Root 的绝对路径。</summary>
    public required string Root { get; init; }

    /// <summary>当前 Theme 来源。</summary>
    public required ThemeSourceKind SourceKind { get; init; }

    /// <summary>已成功读取的 manifest；manifest 缺失或 JSON 无法解析时为空。</summary>
    public ThemeManifest? Manifest { get; init; }

    /// <summary>Theme runner 类型；旧版 build.command 兼容 manifest 记为 <c>process</c>。</summary>
    public string? RunnerKind { get; init; }

    /// <summary>发现与校验诊断。</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Dev Link 是否覆盖了同 id 的 Installed/BuiltIn Theme。</summary>
    public bool ShadowsInstalledTheme { get; init; }

    /// <summary>当前 Catalog 项是否可被选择为 Active Theme。</summary>
    public bool IsAvailable => Manifest is not null && Diagnostics.All(x => !x.IsBlocking);
}
