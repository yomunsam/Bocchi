namespace Bocchi.Generator.Theme;

/// <summary>Theme 发现或解析过程中的诊断严重程度。</summary>
public enum ThemeDiagnosticSeverity
{
    /// <summary>普通提示，不影响 Theme 可用性。</summary>
    Info,

    /// <summary>警告，Theme 仍可使用但需要 Admin 或 Theme 作者注意。</summary>
    Warning,

    /// <summary>阻断错误，当前 Theme 不能被解析为可构建 Theme。</summary>
    Error,
}

/// <summary>Theme Catalog 暴露给 Generator 和 Dashboard 的可解释诊断。</summary>
public sealed record ThemeDiagnostic(
    ThemeDiagnosticSeverity Severity,
    string Code,
    string Message)
{
    /// <summary>当前诊断是否会阻止 Theme 被激活或构建。</summary>
    public bool IsBlocking => Severity == ThemeDiagnosticSeverity.Error;
}
