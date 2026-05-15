namespace Bocchi.GeneratorContract;

/// <summary>Theme 构建命令配置。M3 时代字段，M5 起作为 <c>runner.kind = process</c> 的兼容别名保留。</summary>
public sealed record ThemeBuildSpec
{
    /// <summary>构建命令行，例如 <c>pnpm build</c>。</summary>
    public required string Command { get; init; }

    /// <summary>显式安装命令。默认构建不会自动执行它，只能由调用方明确要求。</summary>
    public string? InstallCommand { get; init; }
}

/// <summary>Theme Runner 声明，描述当前 Theme 应由哪类执行器处理。</summary>
public sealed record ThemeRunnerSpec
{
    /// <summary>Runner 类型。M5 本地 runner 先约定 <c>process</c> 与 <c>builtin-template</c>。</summary>
    public required string Kind { get; init; }

    /// <summary>Runner 入口。<c>builtin-template</c> 可用它选择内置模板 renderer，例如 <c>fluid</c>。</summary>
    public string? Entry { get; init; }

    /// <summary><c>process</c> runner 的构建命令。第三方 Theme 使用它接入自己的构建工具。</summary>
    public string? Command { get; init; }

    /// <summary><c>process</c> runner 的显式安装命令。不会在普通 Full Build 中自动运行。</summary>
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

    /// <summary>M5 起使用的 Runner 声明。为空时允许回退到旧 <see cref="Build"/> 字段。</summary>
    public ThemeRunnerSpec? Runner { get; init; }

    /// <summary>旧版 process 构建字段。新 Theme 应使用 <see cref="Runner"/>。</summary>
    public ThemeBuildSpec? Build { get; init; }

    public ThemeFeatureFlags Features { get; init; } = new();
}
