using Bocchi.Generator.Theme;
using Bocchi.Theme.DefaultStatic;
using Bocchi.Workspace;

namespace Bocchi.Generator.Pipeline.Stages;

/// <summary>
/// 解析 themeId → 加载 <c>theme.json</c>，并放到 <see cref="BuildSession"/> 上下文供后续阶段使用。
/// 未找到时记录 warning 但不阻塞（M3 没有 Theme 也允许完成 Theme 输入数据 + 站点产物）。
/// </summary>
public sealed class LoadThemeStage : IBuildStage
{
    /// <summary>当前工作区路径约定。</summary>
    private readonly BocchiDataLayout _layout;

    /// <summary>构造 Theme 加载阶段。</summary>
    public LoadThemeStage(BocchiDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
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
        if (string.Equals(themeId, DefaultStaticThemeDefinition.ThemeId, StringComparison.Ordinal))
        {
            await DefaultStaticThemeDefinition.EnsureAsync(_layout.ThemesDirectory, session.CancellationToken).ConfigureAwait(false);
        }

        var loaded = await ThemeManifestLoader.TryLoadAsync(_layout.ThemesDirectory, themeId, session.CancellationToken).ConfigureAwait(false);
        if (loaded is null)
        {
            session.Log(Name, BuildLogLevel.Warning, $"未在 '{_layout.ThemesDirectory}' 下找到 theme '{themeId}'；Theme 输入数据仍会写出，但 Theme 构建阶段将被跳过。");
            return true;
        }

        session.SetItem(BuildSessionKeys.LoadedTheme, new LoadedTheme(loaded.Value.Manifest, loaded.Value.ThemeRoot));
        session.Log(Name, BuildLogLevel.Info,
            $"已加载 Theme '{loaded.Value.Manifest.Id}' v{loaded.Value.Manifest.Version}（contract {loaded.Value.Manifest.ContractVersion}）。");
        return true;
    }
}
