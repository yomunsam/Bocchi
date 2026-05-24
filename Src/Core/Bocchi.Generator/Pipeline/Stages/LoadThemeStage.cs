using Bocchi.Generator.Theme;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 解析 themeId → 加载 <c>theme.json</c>，并放到 <see cref="BuildSession"/> 上下文供后续阶段使用。
/// 未找到时记录 warning 但不阻塞（M3 没有 Theme 也允许完成 Theme 输入数据 + 站点产物）。
/// </summary>
public sealed class LoadThemeStage : IBuildStage
{
    /// <summary>统一 Theme Resolver，保证 Generator 与 Dashboard 解析同一个 Theme Root。</summary>
    private readonly ThemeResolver _themeResolver;

    /// <summary>构造 Theme 加载阶段。</summary>
    public LoadThemeStage(ThemeResolver themeResolver)
    {
        ArgumentNullException.ThrowIfNull(themeResolver);
        _themeResolver = themeResolver;
    }

    public string Name => nameof(LoadThemeStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var themeId = session.GetItem<string>(BuildSessionKeys.ThemeId)
            ?? session.Graph?.Site.Settings.DefaultThemeId;
        if (string.IsNullOrEmpty(themeId))
        {
            session.Log(Name, BuildLogLevel.Info, "未指定 themeId，跳过 Theme 加载。");
            return true;
        }

        session.SetItem(BuildSessionKeys.ThemeId, themeId);
        var result = await _themeResolver.ResolveThemeAsync(themeId, session.CancellationToken).ConfigureAwait(false);
        if (!result.IsResolved || result.Theme is null)
        {
            LogDiagnostics(session, result.Diagnostics);
            session.Log(Name, BuildLogLevel.Warning, $"未能解析 theme '{themeId}'；Theme 输入数据仍会写出，但 Theme 构建阶段将被跳过。");
            return true;
        }

        var resolved = result.Theme;
        session.SetItem(BuildSessionKeys.LoadedTheme, new LoadedTheme(resolved.Manifest, resolved.Root)
        {
            SourceKind = resolved.SourceKind,
        });
        LogDiagnostics(session, resolved.Diagnostics);
        session.Log(Name, BuildLogLevel.Info,
            $"已加载 Theme '{resolved.Id}' v{resolved.Version}（contract {resolved.ContractVersion}，source {resolved.SourceKind}，root '{resolved.Root}'，runner '{ResolveRunnerKind(resolved)}'）。");
        return true;
    }

    /// <summary>把 Resolver 诊断投影到构建日志；阻断错误之外也保留 Dev Link shadow 这类定位信息。</summary>
    private void LogDiagnostics(BuildSession session, IReadOnlyList<ThemeDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var level = diagnostic.Severity switch
            {
                ThemeDiagnosticSeverity.Error => BuildLogLevel.Warning,
                ThemeDiagnosticSeverity.Warning => BuildLogLevel.Warning,
                _ => BuildLogLevel.Info,
            };
            session.Log(Name, level, $"Theme diagnostic [{diagnostic.Code}]: {diagnostic.Message}");
        }
    }

    private static string ResolveRunnerKind(ResolvedTheme resolved)
        => string.IsNullOrWhiteSpace(resolved.Manifest.Runner?.Kind)
            ? (string.IsNullOrWhiteSpace(resolved.Manifest.Build?.Command) ? "unknown" : "process")
            : resolved.Manifest.Runner.Kind.Trim();
}
