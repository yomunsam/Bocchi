using Bocchi.Generator.Theme;
using Bocchi.GeneratorContract;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 调用 Theme 构建命令。前提：<see cref="BuildSession.GetItem{T}(string)"/>(<see cref="BuildSessionKeys.LoadedTheme"/>) 非空。
/// 若 Theme 未加载或运行模式为 <see cref="BuildMode.Live"/>（不渲染 HTML），此阶段直接跳过。
/// </summary>
public sealed class RunThemeBuildStage : IBuildStage
{
    private readonly IThemeRunner _runner;
    private readonly WorkspaceLayout _layout;

    public RunThemeBuildStage(IThemeRunner runner, WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(layout);
        _runner = runner;
        _layout = layout;
    }

    public string Name => nameof(RunThemeBuildStage);

    public async Task<bool> ExecuteAsync(BuildSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.Options.Mode == BuildMode.Live)
        {
            session.Log(Name, BuildLogLevel.Info, "Live 模式：跳过 Theme 构建。");
            return true;
        }

        var loaded = session.GetItem<LoadedTheme>(BuildSessionKeys.LoadedTheme);
        if (loaded is null)
        {
            session.Log(Name, BuildLogLevel.Info, "未加载 Theme：跳过 Theme 构建（仅产出 Theme 输入数据 + 站点产物）。");
            return true;
        }

        var manifest = loaded.Manifest;
        var themeRoot = loaded.ThemeRoot;
        var outputDirectory = ThemeOutputPathResolver.ResolveLocalOutputDirectory(themeRoot, manifest.OutputDir);
        ThemeOutputPathResolver.ResetLocalOutputDirectory(outputDirectory);

        var invocation = new ThemeRunInvocation
        {
            ThemeRoot = themeRoot,
            Manifest = manifest,
            InputDirectoryAbsolute = _layout.ThemeInputDirectory,
            OutputDirectoryAbsolute = outputDirectory,
            BaseUrl = session.Graph?.Site.NormalizedBaseUrl.AbsoluteUri ?? "/",
            Environment = session.Options.Environment,
            RunInstall = false,
        };
        await _runner.RunAsync(invocation, (lvl, msg) => session.Log(Name, lvl, msg), session.CancellationToken).ConfigureAwait(false);
        session.Log(Name, BuildLogLevel.Info, $"Theme '{manifest.Id}' 构建完成。");
        return true;
    }
}
