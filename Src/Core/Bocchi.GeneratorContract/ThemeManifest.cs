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
    /// <summary>Theme 是否使用 Posts 输入。</summary>
    public bool Posts { get; init; } = true;

    /// <summary>Theme 是否使用 Pages 输入。</summary>
    public bool Pages { get; init; } = true;

    /// <summary>Theme 是否使用 Works 输入。</summary>
    public bool Works { get; init; } = true;

    /// <summary>Theme 是否使用 Notes 输入。</summary>
    public bool Notes { get; init; } = true;

    /// <summary>Theme 是否使用 Friends 输入。</summary>
    public bool Friends { get; init; } = true;

    /// <summary>Theme 是否使用 Photos 输入。</summary>
    public bool Photos { get; init; }

    /// <summary>Theme 是否提供搜索体验。</summary>
    public bool Search { get; init; } = true;
}

/// <summary>Theme manifest 中声明的私有 i18n 能力和 key 列表。</summary>
public sealed record ThemeI18nManifest
{
    /// <summary>Theme 原生支持或提供默认值的语言代码列表。</summary>
    public IReadOnlyList<string> SupportedLanguages { get; init; } = [];

    /// <summary>Theme 默认语言；为空时由站点主要语言或 Theme 自行 fallback。</summary>
    public string? DefaultLanguage { get; init; }

    /// <summary>Theme 私有 i18n key 声明。Common key 不需要重复声明在这里。</summary>
    public IReadOnlyList<ThemeI18nKeyManifest> Keys { get; init; } = [];
}

/// <summary>Theme manifest 中的单个私有 i18n key 声明。</summary>
public sealed record ThemeI18nKeyManifest
{
    /// <summary>Theme 私有 key，建议使用 theme id 命名空间。</summary>
    public required string Key { get; init; }

    /// <summary>Dashboard 展示给 Admin 的短标题。</summary>
    public required string Title { get; init; }

    /// <summary>Dashboard 展示给 Admin 的说明文本。</summary>
    public string? Description { get; init; }

    /// <summary>Theme manifest 提供的默认 plain text 值，形态为 language -> text。</summary>
    public IReadOnlyDictionary<string, string> DefaultValues { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Theme 元信息清单。对应 <c>theme.json</c>，参见 <c>Docs/Architecture.md §7.2</c>。
/// </summary>
public sealed record ThemeManifest
{
    /// <summary>Theme id，用于选择 Theme 与隔离 Theme 配置。</summary>
    public required string Id { get; init; }

    /// <summary>Theme 展示名称。</summary>
    public required string Name { get; init; }

    /// <summary>Theme 自身版本。</summary>
    public required string Version { get; init; }

    /// <summary>Theme 兼容的 Theme Contract 版本，例如 <see cref="ThemeContractVersion.V1"/>。</summary>
    public required string ContractVersion { get; init; }

    /// <summary>Theme 期望读取输入数据的目录（相对 Theme 根）。默认 <c>../../cache/theme-input</c>。</summary>
    public string InputDir { get; init; } = "../../cache/theme-input";

    /// <summary>Theme 构建产物目录（相对 Theme 根）。默认 <c>build</c>。</summary>
    public string OutputDir { get; init; } = "build";

    /// <summary>M5 起使用的 Runner 声明。为空时允许回退到旧 <see cref="Build"/> 字段。</summary>
    public ThemeRunnerSpec? Runner { get; init; }

    /// <summary>旧版 process 构建字段。新 Theme 应使用 <see cref="Runner"/>。</summary>
    public ThemeBuildSpec? Build { get; init; }

    /// <summary>Theme 支持的内容功能开关。</summary>
    public ThemeFeatureFlags Features { get; init; } = new();

    /// <summary>Theme 私有 i18n key 声明；为空表示 Theme 未声明私有可覆盖文案。</summary>
    public ThemeI18nManifest? I18n { get; init; }
}
