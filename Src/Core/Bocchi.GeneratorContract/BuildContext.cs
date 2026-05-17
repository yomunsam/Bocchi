namespace Bocchi.GeneratorContract;

/// <summary>
/// 构建上下文，对应 <c>../../cache/theme-input/build-context.json</c>，参见 <c>Docs/Architecture.md §7.3</c>。
/// </summary>
public sealed record BuildContext
{
    /// <summary>构建开始时间。</summary>
    public required DateTimeOffset BuildTime { get; init; }

    /// <summary>站点基础 URL，写入 Theme 输入数据时使用。</summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>当前 Theme 的 id。</summary>
    public required string ThemeId { get; init; }

    /// <summary>构建环境标记（例如 <c>development</c> / <c>production</c>）。</summary>
    public required string Environment { get; init; }

    /// <summary>本次构建启用的功能开关。</summary>
    public required ThemeFeatureFlags Features { get; init; }

    /// <summary>
    /// Bocchi 自身的语义化版本（来源于程序集 InformationalVersion）。Theme 可据此做兼容性检查。
    /// </summary>
    public string? BocchiVersion { get; init; }

    /// <summary>本次构建是否纳入草稿。<c>true</c> 时 <c>status: draft</c> 的内容也会出现在 Theme 输入中。</summary>
    public bool IncludeDrafts { get; init; }
}