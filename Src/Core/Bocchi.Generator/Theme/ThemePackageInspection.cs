using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>Theme zip 包 inspection 的结果；安装前只展示诊断，不执行 Theme 代码。</summary>
public sealed record ThemePackageInspection
{
    /// <summary>本次 inspection 的临时工作目录 id。</summary>
    public required string InspectionId { get; init; }

    /// <summary>原始 zip 文件路径。</summary>
    public required string PackagePath { get; init; }

    /// <summary>inspection 工作目录，位于 <c>&lt;data&gt;/cache/theme-upload/</c> 下。</summary>
    public required string WorkDirectory { get; init; }

    /// <summary>归一化后的 Theme Root staging 目录。</summary>
    public required string SourceRoot { get; init; }

    /// <summary>读取成功的 manifest；缺失或 JSON 错误时为空。</summary>
    public ThemeManifest? Manifest { get; init; }

    /// <summary>Theme id；manifest 不可用时为空。</summary>
    public string? ThemeId => Manifest?.Id;

    /// <summary>Theme name；manifest 不可用时为空。</summary>
    public string? Name => Manifest?.Name;

    /// <summary>Theme version；manifest 不可用时为空。</summary>
    public string? Version => Manifest?.Version;

    /// <summary>Runner 类型；旧版 build.command 兼容包记为 <c>process</c>。</summary>
    public string? RunnerKind { get; init; }

    /// <summary>process runner 是否需要 Admin 显式信任后才能安装或激活。</summary>
    public bool RequiresTrust => string.Equals(RunnerKind, "process", StringComparison.OrdinalIgnoreCase);

    /// <summary>inspection 诊断，包含 warning 和 blocking error。</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>是否存在阻断安装的错误。</summary>
    public bool HasBlockingErrors => Diagnostics.Any(x => x.IsBlocking);

    /// <summary>该 inspection 是否已满足安装的前置条件。</summary>
    public bool IsInstallable => Manifest is not null && !HasBlockingErrors;
}

/// <summary>Theme Package 安装或更新后的结果。</summary>
public sealed record ThemePackageInstallResult
{
    /// <summary>安装的 Theme id。</summary>
    public required string ThemeId { get; init; }

    /// <summary>最终安装目录。</summary>
    public required string TargetRoot { get; init; }

    /// <summary>本次是否覆盖了已有 Installed Theme。</summary>
    public bool WasUpdate { get; init; }

    /// <summary>更新时旧版本备份目录；新安装时为空。</summary>
    public string? BackupRoot { get; init; }
}
