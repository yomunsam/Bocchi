using Bocchi.ContentModel;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bocchi.HomeServer.Tests;

/// <summary>验证后台 Category 树服务的持久化边界和深度限制。</summary>
public sealed class CategoryTreeServiceTests
{
    /// <summary>分类树按内容类型独立保存，避免文章和作品共用同一棵后台树。</summary>
    [Fact]
    public async Task SaveAsync_PersistsTreePerContentKind()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);

        await service.SaveAsync(ContentKind.Post,
        [
            new CategoryTreeNode(
                "root-tech",
                "计算机技术",
                "tech",
                [new CategoryTreeNode("root-tech-ai", "AI", "ai", [])]),
        ]);

        var postTree = await service.GetAsync(ContentKind.Post);
        var workTree = await service.GetAsync(ContentKind.Work);

        postTree.Roots.Should().ContainSingle();
        postTree.Roots[0].Name.Should().Be("计算机技术");
        postTree.Roots[0].Slug.Should().Be("tech");
        postTree.Roots[0].Children.Should().ContainSingle(x => x.Name == "AI");
        workTree.Roots.Should().BeEmpty();
    }

    /// <summary>保存时统一截断第 5 层之后的节点，确保 UI 和服务层都有同一条硬边界。</summary>
    [Fact]
    public async Task SaveAsync_TrimsBlankNamesAndStopsAtFiveLevels()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);

        await service.SaveAsync(ContentKind.Post,
        [
            BuildChain(level: 0, maxLevel: 6),
            new CategoryTreeNode("blank", "   ", string.Empty, []),
        ]);

        var tree = await service.GetAsync(ContentKind.Post);

        tree.Roots.Should().ContainSingle();
        CountLevels(tree.Roots[0]).Should().Be(CategoryTreeService.MaxDepth);
        LastNode(tree.Roots[0]).Children.Should().BeEmpty();
    }

    /// <summary>保存时保证同一棵树内 slug 非空且唯一，避免前台 Category URL 抖动或冲突。</summary>
    [Fact]
    public async Task SaveAsync_NormalizesAndDeduplicatesSlugs()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);

        await service.SaveAsync(ContentKind.Post,
        [
            new CategoryTreeNode("a", "Hello World", string.Empty, []),
            new CategoryTreeNode("b", "Other", "hello-world", []),
        ]);

        var tree = await service.GetAsync(ContentKind.Post);

        tree.Roots.Select(node => node.Slug).Should().Equal("hello-world", "hello-world-2");
    }

    /// <summary>没有文件时，把数据库里的树（含 localizedNames）导出一次，并且不把 UpdatedAt 写进 YAML。</summary>
    [Fact]
    public async Task EnsureAsync_ExportsDatabaseWhenFileIsMissing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        db.CategoryTrees.Add(new CategoryTreeRecord
        {
            Scope = "Post",
            TreeJson = """
                [{"id":"a1b2c3d4e5f6789012345678abcdef01","name":"随笔","slug":"essays","children":[],"localizedNames":{"en-US":"Essays"}}]
                """,
            UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();

        await service.EnsureAsync();

        var yaml = await File.ReadAllTextAsync(workspace.Layout.Workspace.CategoriesFile);
        yaml.Should().Contain("scope: Post");
        yaml.Should().Contain("随笔");
        yaml.Should().Contain("en-US: Essays");
        yaml.Should().NotContain("updatedAt", because: "UpdatedAt 只留在数据库");
        yaml.Should().NotContain("UpdatedAt");
        var loaded = await service.GetAsync(ContentKind.Post);
        loaded.Roots.Should().ContainSingle();
        loaded.Roots[0].LocalizedNames.Should().ContainKey("en-US").WhoseValue.Should().Be("Essays");
    }

    /// <summary>已有文件时以文件灌库，数据库里的旧树让位。</summary>
    [Fact]
    public async Task EnsureAsync_ImportsFileOverDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        db.CategoryTrees.Add(new CategoryTreeRecord
        {
            Scope = "Post",
            TreeJson = """[{"id":"old","name":"旧","slug":"old","children":[]}]""",
        });
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(workspace.Layout.Workspace.CategoriesFile, """
            trees:
              - scope: Post
                roots:
                  - id: a1b2c3d4e5f6789012345678abcdef01
                    name: 随笔
                    slug: essays
                    localizedNames:
                      en-US: Essays
                    children: []
            """);

        await service.EnsureAsync();

        var loaded = await service.GetAsync(ContentKind.Post);
        loaded.Roots.Should().ContainSingle();
        loaded.Roots[0].Name.Should().Be("随笔");
        loaded.Roots[0].Slug.Should().Be("essays");
        loaded.Roots[0].LocalizedNames!["en-US"].Should().Be("Essays");
        var onDisk = await File.ReadAllTextAsync(workspace.Layout.Workspace.CategoriesFile);
        onDisk.Should().Contain("name: 随笔");
        await service.EnsureAsync();
        (await File.ReadAllTextAsync(workspace.Layout.Workspace.CategoriesFile)).Should().Be(onDisk);
    }

    /// <summary>坏文件不得覆盖数据库，也不得被默认内容盖掉。</summary>
    [Fact]
    public async Task EnsureAsync_BadFileLeavesDatabaseAndFileUntouched()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        db.CategoryTrees.Add(new CategoryTreeRecord
        {
            Scope = "Post",
            TreeJson = """[{"id":"keep","name":"保留","slug":"keep","children":[]}]""",
        });
        await db.SaveChangesAsync();
        const string broken = "[\n";
        await File.WriteAllTextAsync(workspace.Layout.Workspace.CategoriesFile, broken);

        await service.EnsureAsync();

        (await File.ReadAllTextAsync(workspace.Layout.Workspace.CategoriesFile)).Should().Be(broken);
        (await service.GetAsync(ContentKind.Post)).Roots.Should().ContainSingle(node => node.Name == "保留");
        var save = await service.SaveAsync(ContentKind.Post, [new CategoryTreeNode("x", "新", "new", [])]);
        save.Succeeded.Should().BeFalse();
        save.Code.Should().Be(WorkspaceWriteResult.CategoriesInvalidCode);
        (await File.ReadAllTextAsync(workspace.Layout.Workspace.CategoriesFile)).Should().Be(broken);
        (await service.GetAsync(ContentKind.Post)).Roots.Should().ContainSingle(node => node.Name == "保留");
    }

    /// <summary>同一 scope 出现两次是校验失败，不能灌进数据库。</summary>
    [Fact]
    public async Task EnsureAsync_DuplicateScopeDoesNotImport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        db.CategoryTrees.Add(new CategoryTreeRecord
        {
            Scope = "Post",
            TreeJson = """[{"id":"keep","name":"保留","slug":"keep","children":[]}]""",
        });
        await db.SaveChangesAsync();
        var yaml = """
            trees:
              - scope: Post
                roots: []
              - scope: Post
                roots:
                  - id: other
                    name: 另一棵
                    slug: other
                    children: []
            """;
        await File.WriteAllTextAsync(workspace.Layout.Workspace.CategoriesFile, yaml);

        await service.EnsureAsync();

        (await File.ReadAllTextAsync(workspace.Layout.Workspace.CategoriesFile)).Should().Be(yaml);
        (await service.GetAsync(ContentKind.Post)).Roots.Should().ContainSingle(node => node.Name == "保留");
    }

    /// <summary>site.yaml 过新时，不导出、不灌库。</summary>
    [Fact]
    public async Task EnsureAsync_RefusesWhenSiteFormatVersionIsTooNew()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        Directory.CreateDirectory(workspace.Layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile, "formatVersion: 99\ntitle: Future\n");
        db.CategoryTrees.Add(new CategoryTreeRecord
        {
            Scope = "Post",
            TreeJson = """[{"id":"keep","name":"保留","slug":"keep","children":[]}]""",
        });
        await db.SaveChangesAsync();

        await service.EnsureAsync();

        File.Exists(workspace.Layout.Workspace.CategoriesFile).Should().BeFalse();
        (await service.GetAsync(ContentKind.Post)).Roots.Should().ContainSingle(node => node.Name == "保留");
        var save = await service.SaveAsync(ContentKind.Post, [new CategoryTreeNode("x", "新", "new", [])]);
        save.Code.Should().Be(WorkspaceFormatVersion.UnsupportedCode);
        File.Exists(workspace.Layout.Workspace.CategoriesFile).Should().BeFalse();
    }

    /// <summary>保存往返要留下 localizedNames，并且不写 UpdatedAt。</summary>
    [Fact]
    public async Task SaveAsync_RoundTripsLocalizedNamesThroughYaml()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);

        var saved = await service.SaveAsync(ContentKind.Post,
        [
            new CategoryTreeNode("root-tech", "计算机技术", "tech", [])
            {
                LocalizedNames = new Dictionary<string, string> { ["en-US"] = "Essays" },
            },
        ]);

        saved.Succeeded.Should().BeTrue();
        var yaml = await File.ReadAllTextAsync(workspace.Layout.Workspace.CategoriesFile);
        yaml.Should().Contain("localizedNames:");
        yaml.Should().Contain("en-US: Essays");
        yaml.Should().NotContain("updatedAt");
        var loaded = await service.GetAsync(ContentKind.Post);
        loaded.Roots.Should().ContainSingle();
        loaded.Roots[0].LocalizedNames.Should().ContainKey("en-US").WhoseValue.Should().Be("Essays");
        var act = () => service.GetBuildPostCategoriesAsync();
        await act.Should().NotThrowAsync();
    }

    private static CategoryTreeService CreateService(BocchiDbContext db, TempWorkspace workspace)
        => new(db, workspace.Layout, TimeProvider.System, NullLogger<CategoryTreeService>.Instance);

    private static async Task<BocchiDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static CategoryTreeNode BuildChain(int level, int maxLevel)
        => new(
            $"node-{level}",
            $"Level {level}",
            $"level-{level}",
            level >= maxLevel ? [] : [BuildChain(level + 1, maxLevel)]);

    private static int CountLevels(CategoryTreeNode node)
        => node.Children.Count == 0 ? 1 : 1 + CountLevels(node.Children[0]);

    private static CategoryTreeNode LastNode(CategoryTreeNode node)
        => node.Children.Count == 0 ? node : LastNode(node.Children[0]);

    /// <summary>测试用的临时 DataRoot，结束时删掉 workspace 文件。</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "bocchi-cat-" + Guid.NewGuid().ToString("N"));
            Layout = new BocchiDataLayout(Root);
            Directory.CreateDirectory(Layout.Workspace.SiteDirectory);
        }

        public string Root { get; }

        public BocchiDataLayout Layout { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
