namespace Bocchi.Workspace;

/// <summary>
/// Bocchi 工作区目录约定。对应 <c>Docs/Architecture.md §3</c>。
/// </summary>
/// <param name="Root">工作区根目录的绝对路径。</param>
/// <remarks>
/// 各子目录均为相对于 <see cref="Root"/> 的子路径。Workspace 的实际加载、校验和创建逻辑由 M2 实现。
/// </remarks>
public sealed record WorkspaceLayout(string Root)
{
    /// <summary>用户内容事实来源目录。</summary>
    public string ContentDirectory => Path.Combine(Root, "content");

    /// <summary>媒体文件目录。</summary>
    public string MediaDirectory => Path.Combine(Root, "media");

    /// <summary>仓库内置/副本 Theme 目录。</summary>
    public string ThemesDirectory => Path.Combine(Root, "themes");

    /// <summary>站点级配置目录（site.json、navigation.json 等）。</summary>
    public string SiteDirectory => Path.Combine(Root, "site");

    /// <summary>Bocchi 管理状态目录（SQLite、构建 manifest、Theme 配置等）。</summary>
    public string BocchiDirectory => Path.Combine(Root, ".bocchi");

    /// <summary>Theme 输入数据目录，对应 Theme Contract 中的 <c>inputDir</c>。</summary>
    public string ThemeInputDirectory => Path.Combine(BocchiDirectory, "input");

    /// <summary>构建产物输出目录。</summary>
    public string OutputDirectory => Path.Combine(Root, "output");

    /// <summary>静态站点输出目录。</summary>
    public string PublicOutputDirectory => Path.Combine(OutputDirectory, "public");

    /// <summary>SQLite 状态数据库路径。</summary>
    public string SqliteDatabasePath => Path.Combine(BocchiDirectory, "bocchi.sqlite");
}
