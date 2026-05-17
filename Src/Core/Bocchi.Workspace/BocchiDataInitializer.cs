using Bocchi.Workspace.Exceptions;

namespace Bocchi.Workspace;

/// <summary>
/// DataRoot 初始化器。负责按 <see cref="BocchiDataLayout"/> / <see cref="WorkspaceLayout"/>
/// 创建/补齐目录结构与基础文件，并写入"内容 workspace 使用说明"。幂等：已存在的用户文件不会被覆盖。
/// </summary>
public sealed class BocchiDataInitializer
{
    private readonly BocchiDataLayout _layout;

    public BocchiDataInitializer(BocchiDataLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
    }

    /// <summary>初始化或补齐 DataRoot 与内容 workspace 结构。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // DataRoot 下的 Bocchi 运行数据。workspace 之外的目录都不进入内容 Git 仓库。
            EnsureDirectory(_layout.DataRoot);
            EnsureDirectory(_layout.StateDirectory);
            EnsureDirectory(_layout.LogsDirectory);
            EnsureDirectory(_layout.CacheDirectory);
            EnsureDirectory(_layout.DerivativesDirectory);
            EnsureDirectory(_layout.ThemeInputDirectory);
            EnsureDirectory(_layout.ThemeConfigDirectory);
            EnsureDirectory(_layout.ThemesDirectory);
            EnsureDirectory(_layout.OutputDirectory);
            EnsureDirectory(_layout.PublicOutputDirectory);

            // 内容 workspace 是用户可迁移、可独立 Git 化的源工程。
            var workspace = _layout.Workspace;
            EnsureDirectory(workspace.Root);
            EnsureDirectory(workspace.PostsDirectory);
            EnsureDirectory(workspace.PagesDirectory);
            EnsureDirectory(workspace.WorksDirectory);
            EnsureDirectory(workspace.NotesDirectory);
            EnsureDirectory(workspace.FriendsDirectory);
            EnsureDirectory(workspace.PhotosDirectory);
            EnsureDirectory(workspace.SiteDirectory);

            EnsureFile(workspace.ReadmeFile, DefaultContentReadme);
            EnsureFile(workspace.GitIgnoreFile, DefaultContentGitIgnore);
            EnsureFile(workspace.SiteSettingsFile, DefaultSiteSettingsYaml);
            EnsureFile(workspace.NavigationFile, DefaultNavigationYaml);
            EnsureFile(workspace.FriendsFile, DefaultFriendsYaml);

            return Task.CompletedTask;
        }
        catch (IOException ex)
        {
            throw new WorkspaceInitializationException($"初始化 DataRoot '{_layout.DataRoot}' 失败。", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new WorkspaceInitializationException($"无权限写入 DataRoot '{_layout.DataRoot}'。", ex);
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
        # Bocchi Workspace

        本目录是一个**独立的、可携带的"源工程"**，承载你的全部创作内容：
        Blog、独立页面、作品集、短文、友链、站点设置等。

        关键约定：

        - 仅放原始 Markdown 与原始媒体；**禁止出现任何构建产物**（如 webp、缩略图、HTML、搜索索引）。
        - 不包含任何 Theme 实现或 Theme 专有配置；那些属于 Bocchi 程序的实现细节，位于 DataRoot 的其他目录。
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

        # 派生媒体（必须由构建系统在 DataRoot/cache 内生成，禁止入库）
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

        defaultThemeId: default-static
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
