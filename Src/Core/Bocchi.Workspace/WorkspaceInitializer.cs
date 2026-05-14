using Bocchi.Workspace.Exceptions;

namespace Bocchi.Workspace;

/// <summary>
/// 工作区初始化器。负责按 <see cref="WorkspaceLayout"/> / <see cref="ContentSpaceLayout"/>
/// 创建/补齐目录结构与基础文件，并写入"内容空间使用说明"。幂等：已存在的用户文件不会被覆盖。
/// </summary>
public sealed class WorkspaceInitializer
{
    private readonly WorkspaceLayout _layout;

    public WorkspaceInitializer(WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <summary>初始化或补齐工作区结构。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 系统空间
            EnsureDirectory(_layout.Root);
            EnsureDirectory(_layout.BocchiDirectory);
            EnsureDirectory(_layout.LogsDirectory);
            EnsureDirectory(_layout.CacheDirectory);
            EnsureDirectory(_layout.ThemeInputDirectory);
            EnsureDirectory(_layout.ThemeConfigDirectory);
            EnsureDirectory(_layout.ThemesDirectory);
            EnsureDirectory(_layout.OutputDirectory);
            EnsureDirectory(_layout.PublicOutputDirectory);

            // 内容空间
            var cs = _layout.ContentSpace;
            EnsureDirectory(cs.Root);
            EnsureDirectory(cs.PostsDirectory);
            EnsureDirectory(cs.PagesDirectory);
            EnsureDirectory(cs.WorksDirectory);
            EnsureDirectory(cs.NotesDirectory);
            EnsureDirectory(cs.FriendsDirectory);
            EnsureDirectory(cs.PhotosDirectory);
            EnsureDirectory(cs.SiteDirectory);

            EnsureFile(cs.ReadmeFile, DefaultContentReadme);
            EnsureFile(cs.GitIgnoreFile, DefaultContentGitIgnore);
            EnsureFile(cs.SiteSettingsFile, DefaultSiteSettingsYaml);
            EnsureFile(cs.NavigationFile, DefaultNavigationYaml);
            EnsureFile(cs.FriendsFile, DefaultFriendsYaml);

            return Task.CompletedTask;
        }
        catch (IOException ex)
        {
            throw new WorkspaceInitializationException($"初始化工作区 '{_layout.Root}' 失败。", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new WorkspaceInitializationException($"无权限写入工作区 '{_layout.Root}'。", ex);
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void EnsureFile(string path, string defaultContent)
    {
        if (File.Exists(path))
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, defaultContent);
    }

    private const string DefaultContentReadme = """
        # 内容空间（Content Space）

        本目录是一个**独立的、可携带的"源工程"**，承载你的全部创作内容：
        Blog、独立页面、作品集、短文、友链、站点设置等。

        关键约定：

        - 仅放原始 Markdown 与原始媒体；**禁止出现任何构建产物**（如 webp、缩略图、HTML、搜索索引）。
        - 不包含任何 Theme 实现或 Theme 专有配置；那些属于 Bocchi 程序的实现细节。
        - Post / Work / Note / Photo 一律使用年份目录作为一级分类。
        - Post / Work 单篇为目录形式：`<kind>/<year>/<slug>/index.md` + `assets/`。
        - Page 不按年份分类：`pages/<slug>/index.md`。
        - Note 为单文件：`notes/<year>/<filename>.md`。
        - frontmatter 一律 YAML（首尾 `---`）。

        本目录可以独立用 Git 管理；将来 Bocchi 这个程序若被替换或弃用，
        把本目录整体打包带走即可，不会丢失任何内容资产。
        """;

    private const string DefaultContentGitIgnore = """
        # 操作系统/编辑器临时文件
        .DS_Store
        Thumbs.db
        *.swp
        *.swo
        *~

        # 派生媒体（必须由构建系统在 Bocchi 系统空间内生成，禁止入库）
        **/*.webp
        **/*.thumb.*
        **/*.preview.*
        **/.cache/
        """;

    private const string DefaultSiteSettingsYaml = """
        # 站点设置。详见 Docs/Architecture.md §4.6。
        title: My Site
        description: ""
        language: zh-CN
        timeZone: Asia/Shanghai
        baseUrl: https://example.com/

        author:
          name: Anonymous
          # email: you@example.com
          # bio: ""

        social: []

        defaultThemeId:
        enableRss: true
        enableSitemap: true
        enableSearch: true
        """;

    private const string DefaultNavigationYaml = """
        # 顶部导航条目。覆盖 site.yaml 中的 navigation 字段（如果同时存在）。
        # - title: Home
        #   href: /
        # - title: Blog
        #   href: /posts/
        items: []
        """;

    private const string DefaultFriendsYaml = """
        # 友链列表。每项：name + url，可选 avatar / description / tags / order / status。
        friends: []
        """;
}
