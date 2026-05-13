namespace Bocchi.GeneratorContract;

/// <summary>Theme 构建命令配置。</summary>
public sealed record ThemeBuildSpec
{
    public required string Command { get; init; }

    public string? InstallCommand { get; init; }
}

/// <summary>Theme 支持的功能开关，决定生成器是否需要为该 Theme 准备对应输入数据。</summary>
public sealed record ThemeFeatureFlags
{
    public bool Posts { get; init; } = true;

    public bool Pages { get; init; } = true;

    public bool Works { get; init; } = true;

    public bool Notes { get; init; } = true;

    public bool Friends { get; init; } = true;

    public bool Photos { get; init; }

    public bool Search { get; init; } = true;
}

/// <summary>
/// Theme 元信息清单。对应 <c>theme.json</c>，参见 <c>Docs/Architecture.md §7.2</c>。
/// </summary>
public sealed record ThemeManifest
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    /// <summary>Theme 兼容的 Theme Contract 版本，例如 <see cref="ThemeContractVersion.V1"/>。</summary>
    public required string ContractVersion { get; init; }

    /// <summary>Theme 期望读取输入数据的目录（相对 Theme 根）。默认 <c>.bocchi/input</c>。</summary>
    public string InputDir { get; init; } = ".bocchi/input";

    /// <summary>Theme 构建产物目录（相对 Theme 根）。默认 <c>build</c>。</summary>
    public string OutputDir { get; init; } = "build";

    public required ThemeBuildSpec Build { get; init; }

    public ThemeFeatureFlags Features { get; init; } = new();
}
