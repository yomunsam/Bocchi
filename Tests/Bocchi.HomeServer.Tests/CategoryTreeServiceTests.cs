using Bocchi.ContentModel;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
        var service = new CategoryTreeService(db, TimeProvider.System);

        await service.SaveAsync(ContentKind.Post,
        [
            new CategoryTreeNode(
                "root-tech",
                "计算机技术",
                [new CategoryTreeNode("root-tech-ai", "AI", [])]),
        ]);

        var postTree = await service.GetAsync(ContentKind.Post);
        var workTree = await service.GetAsync(ContentKind.Work);

        postTree.Roots.Should().ContainSingle();
        postTree.Roots[0].Name.Should().Be("计算机技术");
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
        var service = new CategoryTreeService(db, TimeProvider.System);

        await service.SaveAsync(ContentKind.Post,
        [
            BuildChain(level: 0, maxLevel: 6),
            new CategoryTreeNode("blank", "   ", []),
        ]);

        var tree = await service.GetAsync(ContentKind.Post);

        tree.Roots.Should().ContainSingle();
        CountLevels(tree.Roots[0]).Should().Be(CategoryTreeService.MaxDepth);
        LastNode(tree.Roots[0]).Children.Should().BeEmpty();
    }

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
            level >= maxLevel ? [] : [BuildChain(level + 1, maxLevel)]);

    private static int CountLevels(CategoryTreeNode node)
        => node.Children.Count == 0 ? 1 : 1 + CountLevels(node.Children[0]);

    private static CategoryTreeNode LastNode(CategoryTreeNode node)
        => node.Children.Count == 0 ? node : LastNode(node.Children[0]);
}
