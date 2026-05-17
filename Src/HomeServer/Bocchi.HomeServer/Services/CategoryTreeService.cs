using System.Text.Json;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 后台分类树服务。它只保存 Admin 编辑器状态，不把分类树写入内容 frontmatter、前台 Menu 或构建输入。
/// </summary>
public sealed class CategoryTreeService
{
    /// <summary>分类树最大深度：根节点是 0 层，最深子节点是 4 层。</summary>
    public const int MaxDepth = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BocchiDbContext _db;
    private readonly TimeProvider _time;

    /// <summary>构造分类树服务。</summary>
    public CategoryTreeService(BocchiDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>读取指定内容类型的分类树；缺失时返回空树。</summary>
    public async Task<CategoryTreeView> GetAsync(ContentKind kind, CancellationToken cancellationToken = default)
    {
        var scope = NormalizeScope(kind);
        var record = await _db.CategoryTrees
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == scope, cancellationToken)
            .ConfigureAwait(false);
        var roots = Deserialize(record?.TreeJson);
        return new CategoryTreeView(scope, roots, record?.UpdatedAt);
    }

    /// <summary>保存指定内容类型的分类树，并在服务层统一清理空名称和超深节点。</summary>
    public async Task SaveAsync(
        ContentKind kind,
        IEnumerable<CategoryTreeNode> roots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var scope = NormalizeScope(kind);
        var normalizedRoots = NormalizeNodes(roots, depth: 0);
        var record = await _db.CategoryTrees
            .FirstOrDefaultAsync(x => x.Scope == scope, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new CategoryTreeRecord { Scope = scope };
            _db.CategoryTrees.Add(record);
        }

        record.TreeJson = JsonSerializer.Serialize(normalizedRoots, JsonOptions);
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeScope(ContentKind kind) => kind switch
    {
        ContentKind.Work => nameof(ContentKind.Work),
        _ => nameof(ContentKind.Post),
    };

    private static List<CategoryTreeNode> Deserialize(string? treeJson)
        => string.IsNullOrWhiteSpace(treeJson)
            ? []
            : NormalizeNodes(JsonSerializer.Deserialize<List<CategoryTreeNode>>(treeJson, JsonOptions) ?? [], depth: 0);

    private static List<CategoryTreeNode> NormalizeNodes(IEnumerable<CategoryTreeNode> nodes, int depth)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }

        return nodes
            .Select(node => NormalizeNode(node, depth))
            .Where(node => node is not null)
            .Select(node => node!)
            .ToList();
    }

    private static CategoryTreeNode? NormalizeNode(CategoryTreeNode node, int depth)
    {
        var name = NormalizeName(node.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var id = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id.Trim();
        var children = depth + 1 >= MaxDepth
            ? []
            : NormalizeNodes(node.Children, depth + 1);
        return new CategoryTreeNode(id, name, children);
    }

    private static string NormalizeName(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}

/// <summary>分类树读取结果。</summary>
/// <param name="Scope">分类树所属范围。</param>
/// <param name="Roots">0 层根类别；多个根类别在编辑器中并排展示。</param>
/// <param name="UpdatedAt">最后保存时间；空值表示尚未保存过。</param>
public sealed record CategoryTreeView(
    string Scope,
    IReadOnlyList<CategoryTreeNode> Roots,
    DateTimeOffset? UpdatedAt);

/// <summary>分类树节点。节点 id 负责保持 UI 和后续关联的稳定性，名称负责后台展示。</summary>
/// <param name="Id">稳定节点 id。</param>
/// <param name="Name">类别显示名称。</param>
/// <param name="Children">下一层子类别。</param>
public sealed record CategoryTreeNode(
    string Id,
    string Name,
    IReadOnlyList<CategoryTreeNode> Children);
