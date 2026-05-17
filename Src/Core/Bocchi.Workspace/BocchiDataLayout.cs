namespace Bocchi.Workspace;

/// <summary>
/// Bocchi 持久化数据根目录约定。对应 <c>Docs/Architecture.md §3</c>。
/// </summary>
/// <remarks>
/// <para>
/// DataRoot 在物理上严格切分为用户内容 workspace 与 Bocchi 运行数据：
/// </para>
/// <list type="bullet">
///   <item>
///     <term>Workspace</term>
///     <description>
///       默认位于 <see cref="WorkspaceRoot"/>（即 <c>&lt;data&gt;/workspace/</c>），承载用户的纯创作资产，
///       可独立打包、迁移、可作为独立 Git 仓库。
///     </description>
///   </item>
///   <item>
///     <term>Bocchi 运行数据</term>
///     <description>
///       <see cref="StateDirectory"/>、<see cref="CacheDirectory"/>、<see cref="OutputDirectory"/> 等，
///       位于 workspace 根之外，不会被用户内容仓库索引。
///     </description>
///   </item>
/// </list>
/// <para>
/// 任何模块都应当通过 <see cref="BocchiDataLayout"/> / <see cref="WorkspaceLayout"/> 取路径，
/// 禁止直接拼接，以免破坏切分。
/// </para>
/// </remarks>
public sealed record BocchiDataLayout
{
    /// <summary>构造一个标准 DataRoot 布局（内容 workspace 在 <c>&lt;data&gt;/workspace/</c>）。</summary>
    /// <param name="dataRoot">DataRoot 绝对或相对路径。</param>
    public BocchiDataLayout(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        DataRoot = Path.GetFullPath(dataRoot);
        Workspace = new WorkspaceLayout(Path.Combine(DataRoot, "workspace"));
    }

    /// <summary>Bocchi 持久化数据根目录的绝对路径。</summary>
    public string DataRoot { get; }

    /// <summary>用户内容 workspace 布局。位于 <see cref="DataRoot"/> 之下的 <c>workspace/</c>，可独立 Git 化。</summary>
    public WorkspaceLayout Workspace { get; }

    /// <summary>用户内容 workspace 根目录（便捷访问，等价于 <see cref="Workspace"/>.<see cref="WorkspaceLayout.Root"/>）。</summary>
    public string WorkspaceRoot => Workspace.Root;

    /// <summary>可见 Theme 实例目录。</summary>
    public string ThemesDirectory => Path.Combine(DataRoot, "themes");

    /// <summary>Bocchi / Home Server 状态目录（SQLite、Data Protection keys、Theme 配置等）。</summary>
    public string StateDirectory => Path.Combine(DataRoot, "state");

    /// <summary>SQLite 状态数据库路径。</summary>
    public string SqliteDatabasePath => Path.Combine(StateDirectory, "bocchi.sqlite");

    /// <summary>Bocchi 日志目录。</summary>
    public string LogsDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>Bocchi 缓存目录。</summary>
    public string CacheDirectory => Path.Combine(DataRoot, "cache");

    /// <summary>派生媒体（webp、缩略图等构建产物）目录。</summary>
    public string DerivativesDirectory => Path.Combine(CacheDirectory, "derivatives");

    /// <summary>Theme 输入数据目录，对应 Theme Contract 中的 <c>inputDir</c>。</summary>
    public string ThemeInputDirectory => Path.Combine(CacheDirectory, "theme-input");

    /// <summary>Theme 实例配置目录（每个 Theme 一份 JSON）。</summary>
    public string ThemeConfigDirectory => Path.Combine(StateDirectory, "theme-config");

    /// <summary>构建产物输出根目录。</summary>
    public string OutputDirectory => Path.Combine(DataRoot, "output");

    /// <summary>静态站点输出目录。</summary>
    public string PublicOutputDirectory => Path.Combine(OutputDirectory, "public");
}
