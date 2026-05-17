using Bocchi.Generator.Theme;
using Bocchi.GeneratorContract;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 调用 Theme 构建命令。前提：<see cref="BuildSession.GetItem{T}(string)"/>(<see cref="BuildSessionKeys.LoadedTheme"/>) 非空。
/// Live 模式只有在调用方提供一次性 input / output 目录时才渲染 Theme，避免依赖或污染静态发布产物。
/// </summary>
public sealed class RunThemeBuildStage : IBuildStage
{
    private readonly IThemeRunner _runner;
    private readonly BocchiDataLayout _layout;

    public RunThemeBuildStage(IThemeRunner runner, BocchiDataLayout layout)
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

        var loaded = session.GetItem<LoadedTheme>(BuildSessionKeys.LoadedTheme);
        if (loaded is null)
        {
            session.Log(Name, BuildLogLevel.Info, "未加载 Theme：跳过 Theme 构建（仅产出 Theme 输入数据 + 站点产物）。");
            return true;
        }

        var manifest = loaded.Manifest;
        var themeRoot = loaded.ThemeRoot;
        if (!TryResolveThemeDirectories(session, themeRoot, manifest.OutputDir, out var inputDirectory, out var outputDirectory))
        {
            session.Log(Name, BuildLogLevel.Info, "Live 模式未提供 Theme 预览目录：跳过 Theme 构建。");
            return true;
        }

        Directory.CreateDirectory(inputDirectory);
        ThemeOutputPathResolver.ResetLocalOutputDirectory(outputDirectory);

        var invocation = new ThemeRunInvocation
        {
            ThemeRoot = themeRoot,
            Manifest = manifest,
            InputDirectoryAbsolute = inputDirectory,
            OutputDirectoryAbsolute = outputDirectory,
            BaseUrl = session.Graph?.Site.NormalizedBaseUrl.AbsoluteUri ?? "/",
            Environment = session.Options.Environment,
            RunInstall = false,
        };
        await _runner.RunAsync(invocation, (lvl, msg) => session.Log(Name, lvl, msg), session.CancellationToken).ConfigureAwait(false);
        session.Log(Name, BuildLogLevel.Info, $"Theme '{manifest.Id}' 构建完成。");
        return true;
    }

    /// <summary>解析 Theme runner 的输入/输出目录；Live 预览必须使用调用方提供的一次性目录。</summary>
    private bool TryResolveThemeDirectories(
        BuildSession session,
        string themeRoot,
        string outputDir,
        out string inputDirectory,
        out string outputDirectory)
    {
        if (session.Options.Mode != BuildMode.Live)
        {
            inputDirectory = _layout.ThemeInputDirectory;
            outputDirectory = ThemeOutputPathResolver.ResolveLocalOutputDirectory(themeRoot, outputDir);
            return true;
        }

        if (string.IsNullOrWhiteSpace(session.Options.LiveThemeInputDirectory) ||
            string.IsNullOrWhiteSpace(session.Options.LiveThemeOutputDirectory))
        {
            inputDirectory = string.Empty;
            outputDirectory = string.Empty;
            return false;
        }

        // Live 预览使用 DataRoot/cache 下的一次性目录，不复用 Theme 的发布输出目录，避免与 Full Build 互相踩写。
        inputDirectory = Path.GetFullPath(session.Options.LiveThemeInputDirectory);
        outputDirectory = Path.GetFullPath(session.Options.LiveThemeOutputDirectory);
        return true;
    }
}
