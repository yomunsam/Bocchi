namespace Bocchi.Workspace;

/// <summary>
/// 用户内容 workspace 目录约定。对应 <c>Docs/Architecture.md §3</c> 与
/// <c>Docs/Milestones/M2/M2.md §3.2</c>。
/// </summary>
/// <remarks>
/// <para>
/// Workspace 是用户的纯创作资产；它必须满足"独立可携、可作为独立 Git 仓库、不含构建产物、
/// 不含 Theme 专有配置或 Theme 实现"。
/// </para>
/// <para>
/// 强约束：
/// </para>
/// <list type="bullet">
///   <item><description>Post / Work / Note / Photo 一律使用 <b>年份目录</b> 作为一级分类（年份正则 <c>^\d{4}$</c>）。</description></item>
///   <item><description>Post / Work 单篇为目录形式：<c>&lt;kind&gt;/&lt;year&gt;/&lt;slug&gt;/index.md</c> + <c>assets/</c>。</description></item>
///   <item><description>Page 不按年份分类：<c>pages/&lt;slug&gt;/index.md</c>。</description></item>
///   <item><description>Note 为单文件：<c>notes/&lt;year&gt;/&lt;filename&gt;.md</c>。</description></item>
///   <item><description>frontmatter 一律 YAML，使用 <c>---</c> 边界。</description></item>
///   <item><description>媒体路径在 frontmatter 中以"相对当前文件"写。</description></item>
/// </list>
/// </remarks>
public sealed record WorkspaceLayout
{
    /// <summary>构造一个内容 workspace 布局。</summary>
    /// <param name="root">内容 workspace 根目录的绝对路径。</param>
    public WorkspaceLayout(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>内容 workspace 根目录。</summary>
    public string Root { get; }

    /// <summary>文章根目录（按年份分类）。</summary>
    public string PostsDirectory => Path.Combine(Root, "posts");

    /// <summary>独立页面根目录（不按年份分类）。</summary>
    public string PagesDirectory => Path.Combine(Root, "pages");

    /// <summary>作品根目录（按年份分类）。</summary>
    public string WorksDirectory => Path.Combine(Root, "works");

    /// <summary>短文根目录（按年份分类，单文件即一条）。</summary>
    public string NotesDirectory => Path.Combine(Root, "notes");

    /// <summary>友链根目录。</summary>
    public string FriendsDirectory => Path.Combine(Root, "friends");

    /// <summary>友链 YAML 文件路径。</summary>
    public string FriendsFile => Path.Combine(FriendsDirectory, "friends.yaml");

    /// <summary>照片墙根目录（M2 仅占位）。</summary>
    public string PhotosDirectory => Path.Combine(Root, "photos");

    /// <summary>站点级配置目录。</summary>
    public string SiteDirectory => Path.Combine(Root, "site");

    /// <summary>站点设置 YAML 文件路径。</summary>
    public string SiteSettingsFile => Path.Combine(SiteDirectory, "site.yaml");

    /// <summary>导航 YAML 文件路径。</summary>
    public string NavigationFile => Path.Combine(SiteDirectory, "navigation.yaml");

    /// <summary>内容 workspace 根 README（自动生成，说明本目录是源工程）。</summary>
    public string ReadmeFile => Path.Combine(Root, "README.md");

    /// <summary>内容 workspace 根 .gitignore（自动生成）。</summary>
    public string GitIgnoreFile => Path.Combine(Root, ".gitignore");

    /// <summary>把绝对路径转换为相对内容 workspace 根的、以 <c>/</c> 归一化的路径。</summary>
    public string ToRelative(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        var rel = Path.GetRelativePath(Root, absolutePath);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }
}
