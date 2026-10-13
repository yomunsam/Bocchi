using Bocchi.ContentModel;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bocchi.HomeServer.Tests;

/// <summary>验证后台 Category 树服务的持久化边界、深度限制与 workspace 权威。</summary>
public sealed class CategoryTreeServiceTests
{
    [Fact]
    public async Task SaveAsync_PersistsTreePerContentKind()
    {
        await using var harness = await CreateHarnessAsync();
        var service = harness.Service;

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

        File.Exists(harness.Layout.Workspace.CategoriesFile).Should().BeTrue();
        var yaml = await File.ReadAllTextAsync(harness.Layout.Workspace.CategoriesFile);
        yaml.Should().Contain("scope: Post");
        yaml.Should().Contain("name: 计算机技术");
    }

    [Fact]
    public async Task SaveAsync_TrimsBlankNamesAndStopsAtFiveLevels()
    {
        await using var harness = await CreateHarnessAsync();
        var service = harness.Service;

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

    [Fact]
    public async Task SaveAsync_NormalizesAndDeduplicatesSlugs()
    {
        await using var harness = await CreateHarnessAsync();
        var service = harness.Service;

        await service.SaveAsync(ContentKind.Post,
        [
            new CategoryTreeNode("a", "Hello World", string.Empty, []),
            new CategoryTreeNode("b", "Other", "hello-world", []),
        ]);

        var tree = await service.GetAsync(ContentKind.Post);

        tree.Roots.Select(node => node.Slug).Should().Equal("hello-world", "hello-world-2");
    }

    [Fact]
    public async Task EnsureAsync_NoFile_ExportsDbToWorkspace()
    {
        await using var harness = await CreateHarnessAsync();
        // 先直接灌 DB，模拟旧实例只有 DB。
        harness.Db.CategoryTrees.Add(new CategoryTreeRecord
        {
            Scope = "Post",
            TreeJson = """[{"id":"e1","name":"随笔","slug":"essays","children":[]}]""",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Db.SaveChangesAsync();

        File.Exists(harness.Layout.Workspace.CategoriesFile).Should().BeFalse();
        await harness.Service.EnsureAsync();

        File.Exists(harness.Layout.Workspace.CategoriesFile).Should().BeTrue();
        var yaml = await File.ReadAllTextAsync(harness.Layout.Workspace.CategoriesFile);
        yaml.Should().Contain("name: 随笔");
        yaml.Should().Contain("slug: essays");
    }

    [Fact]
    public async Task EnsureAsync_FileImportsIntoDb_WithLocalizedNames()
    {
        await using var harness = await CreateHarnessAsync();
        Directory.CreateDirectory(harness.Layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(harness.Layout.Workspace.SiteSettingsFile, "formatVersion: 1\ntitle: T\n");
        await File.WriteAllTextAsync(harness.Layout.Workspace.CategoriesFile, """
            trees:
              - scope: Post
                roots:
                  - id: "a1b2c3d4e5f6789012345678abcdef01"
                    name: 随笔
                    slug: essays
                    localizedNames:
                      en-US: Essays
                    children: []
              - scope: Work
                roots: []
            """);

        await harness.Service.EnsureAsync();

        var tree = await harness.Service.GetAsync(ContentKind.Post);
        tree.Roots.Should().ContainSingle();
        tree.Roots[0].Name.Should().Be("随笔");
        tree.Roots[0].LocalizedNames.Should().ContainKey("en-US");
        tree.Roots[0].LocalizedNames!["en-US"].Should().Be("Essays");
    }

    [Fact]
    public async Task EnsureAsync_BadFile_DoesNotImportOrOverwrite()
    {
        await using var harness = await CreateHarnessAsync();
        Directory.CreateDirectory(harness.Layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(harness.Layout.Workspace.SiteSettingsFile, "formatVersion: 1\ntitle: T\n");
        const string bad = "trees: not-a-sequence\n";
        await File.WriteAllTextAsync(harness.Layout.Workspace.CategoriesFile, bad);

        harness.Db.CategoryTrees.Add(new CategoryTreeRecord
        {
            Scope = "Post",
            TreeJson = """[{"id":"keep","name":"保留","slug":"keep","children":[]}]""",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Db.SaveChangesAsync();

        await harness.Service.EnsureAsync();

        var onDisk = await File.ReadAllTextAsync(harness.Layout.Workspace.CategoriesFile);
        onDisk.Should().Be(bad);

        var tree = await harness.Service.GetAsync(ContentKind.Post);
        tree.Roots.Should().ContainSingle(x => x.Name == "保留");
    }

    [Fact]
    public async Task SaveAsync_RoundTripsLocalizedNames()
    {
        await using var harness = await CreateHarnessAsync();
        await harness.Service.SaveAsync(ContentKind.Post,
        [
            new CategoryTreeNode("id1", "随笔", "essays", [])
            {
                LocalizedNames = new Dictionary<string, string> { ["en-US"] = "Essays" },
            },
        ]);

        harness.Db.ChangeTracker.Clear();
        await harness.Service.EnsureAsync();
        var tree = await harness.Service.GetAsync(ContentKind.Post);
        tree.Roots[0].LocalizedNames!["en-US"].Should().Be("Essays");

        var yaml = await File.ReadAllTextAsync(harness.Layout.Workspace.CategoriesFile);
        yaml.Should().Contain("localizedNames:");
        yaml.Should().Contain("en-US: Essays");
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-cat-tests", Guid.NewGuid().ToString("N"));
        return await CreateHarnessFromDataRootAsync(dataRoot);
    }

    private static async Task<Harness> CreateHarnessFromDataRootAsync(string dataRoot)
    {
        Directory.CreateDirectory(dataRoot);
        var layout = new BocchiDataLayout(dataRoot);
        Directory.CreateDirectory(layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(layout.Workspace.SiteSettingsFile, "formatVersion: 1\ntitle: Test\n");

        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new CategoryTreeService(db, layout, TimeProvider.System, NullLogger<CategoryTreeService>.Instance);
        return new Harness(dataRoot, layout, connection, db, service);
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

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(
            string dataRoot,
            BocchiDataLayout layout,
            SqliteConnection connection,
            BocchiDbContext db,
            CategoryTreeService service)
        {
            DataRoot = dataRoot;
            Layout = layout;
            Connection = connection;
            Db = db;
            Service = service;
        }

        public string DataRoot { get; }
        public BocchiDataLayout Layout { get; }
        public SqliteConnection Connection { get; }
        public BocchiDbContext Db { get; }
        public CategoryTreeService Service { get; }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
            try
            {
                Directory.Delete(DataRoot, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }
}
