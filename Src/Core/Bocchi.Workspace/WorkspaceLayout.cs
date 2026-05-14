namespace Bocchi.Workspace;

/// <summary>
/// Bocchi 工作区目录约定。对应 <c>Docs/Architecture.md §3</c>。
/// </summary>
/// <remarks>
/// <para>
/// 工作区在物理上严格切分为两部分：
/// </para>
/// <list type="bullet">
///   <item>
///     <term>内容空间（Content Space）</term>
///     <description>
///       默认位于 <see cref="ContentSpaceRoot"/>（即 <c>&lt;Root&gt;/content/</c>），承载用户的纯创作资产，
///       可独立打包、迁移、可作为独立 Git 仓库。其内部布局由 <see cref="ContentSpaceLayout"/> 决定。
///     </description>
///   </item>
///   <item>
///     <term>Bocchi 系统空间</term>
///     <description>
///       <see cref="ThemesDirectory"/>、<see cref="BocchiDirectory"/>、<see cref="OutputDirectory"/>，
///       与 Bocchi 程序同寿，可被替换；不会被内容空间的 Git 仓库索引（位于内容空间根之外）。
///     </description>
///   </item>
/// </list>
/// <para>
/// 任何模块都应当通过 <see cref="WorkspaceLayout"/> / <see cref="ContentSpaceLayout"/> 取路径，
/// 禁止直接拼接，以免破坏切分。
/// </para>
/// </remarks>
public sealed record WorkspaceLayout
{
    /// <summary>构造一个标准布局（内容空间在 <c>&lt;Root&gt;/content/</c>）。</summary>
    /// <param name="root">工作区根目录的绝对路径。</param>
    public WorkspaceLayout(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        ContentSpace = new ContentSpaceLayout(Path.Combine(Root, "content"));
    }

    /// <summary>工作区根目录的绝对路径。</summary>
    public string Root { get; }

    /// <summary>内容空间布局。位于 <see cref="Root"/> 之下的 <c>content/</c>，可独立 Git 化。</summary>
    public ContentSpaceLayout ContentSpace { get; }

    /// <summary>内容空间根目录（便捷访问，等价于 <see cref="ContentSpace"/>.<see cref="ContentSpaceLayout.Root"/>）。</summary>
    public string ContentSpaceRoot => ContentSpace.Root;

    /// <summary>仓库内置/副本 Theme 目录。</summary>
    public string ThemesDirectory => Path.Combine(Root, "themes");

    /// <summary>Bocchi 管理状态目录（SQLite、构建 manifest、Theme 配置、日志等）。</summary>
    public string BocchiDirectory => Path.Combine(Root, ".bocchi");

    /// <summary>SQLite 状态数据库路径。</summary>
    public string SqliteDatabasePath => Path.Combine(BocchiDirectory, "bocchi.sqlite");

    /// <summary>Bocchi 日志目录（M2 起 Serilog 文件 sink 落到这里）。</summary>
    public string LogsDirectory => Path.Combine(BocchiDirectory, "logs");

    /// <summary>Bocchi 缓存目录。</summary>
    public string CacheDirectory => Path.Combine(BocchiDirectory, "cache");

    /// <summary>派生媒体（webp、缩略图等构建产物）目录。M3 起使用，M2 仅约定路径。</summary>
    public string DerivativesDirectory => Path.Combine(CacheDirectory, "derivatives");

    /// <summary>Theme 输入数据目录，对应 Theme Contract 中的 <c>inputDir</c>。</summary>
    public string ThemeInputDirectory => Path.Combine(BocchiDirectory, "input");

    /// <summary>Theme 实例配置目录（每个 Theme 一份 JSON）。</summary>
    public string ThemeConfigDirectory => Path.Combine(BocchiDirectory, "theme-config");

    /// <summary>构建产物输出根目录。</summary>
    public string OutputDirectory => Path.Combine(Root, "output");

    /// <summary>静态站点输出目录。</summary>
    public string PublicOutputDirectory => Path.Combine(OutputDirectory, "public");
}

