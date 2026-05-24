namespace Bocchi.Generator.Theme;

/// <summary>单个 Theme id 的解析结果；未解析成功时保留诊断给 Build log 和 Dashboard。</summary>
public sealed record ThemeResolveResult
{
    /// <summary>被解析的 Theme id。</summary>
    public required string Id { get; init; }

    /// <summary>成功解析的 Theme；失败时为空。</summary>
    public ResolvedTheme? Theme { get; init; }

    /// <summary>解析过程中的诊断。</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>是否已成功解析为可构建 Theme。</summary>
    public bool IsResolved => Theme is not null;
}
