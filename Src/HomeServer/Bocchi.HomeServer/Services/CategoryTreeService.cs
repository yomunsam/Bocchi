using System.Text.Json;

using Bocchi.ContentModel;
using Bocchi.Generator.ContentGraph;
using Bocchi.HomeServer.Data;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 后台分类树服务。它保存 Admin 编辑器状态，并把 Post Category tree 作为 Generator 构建快照输入。
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
        var usedSlugs = new HashSet<string>(StringComparer.Ordinal);
        var normalizedRoots = NormalizeNodes(roots, depth: 0, usedSlugs);
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

    /// <summary>读取 Generator 构建所需的 Post Category tree 快照。</summary>
    public async Task<IReadOnlyList<BuildCategoryNode>> GetBuildPostCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var view = await GetAsync(ContentKind.Post, cancellationToken).ConfigureAwait(false);
        return view.Roots.Select(ToBuildNode).ToArray();
    }

    private static List<CategoryTreeNode> Deserialize(string? treeJson)
        => string.IsNullOrWhiteSpace(treeJson)
            ? []
            : NormalizeNodes(
                JsonSerializer.Deserialize<List<CategoryTreeNode>>(treeJson, JsonOptions) ?? [],
                depth: 0,
                new HashSet<string>(StringComparer.Ordinal));

    private static List<CategoryTreeNode> NormalizeNodes(
        IEnumerable<CategoryTreeNode> nodes,
        int depth,
        HashSet<string> usedSlugs)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }

        return nodes
            .Select(node => NormalizeNode(node, depth, usedSlugs))
            .Where(node => node is not null)
            .Select(node => node!)
            .ToList();
    }

    private static CategoryTreeNode? NormalizeNode(CategoryTreeNode node, int depth, HashSet<string> usedSlugs)
    {
        var name = NormalizeName(node.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var id = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id.Trim();
        var slug = EnsureUniqueSlug(CreateBaseSlug(node.Slug, name, id), usedSlugs);
        var children = depth + 1 >= MaxDepth
            ? []
            : NormalizeNodes(node.Children, depth + 1, usedSlugs);
        return new CategoryTreeNode(id, name, slug, children);
    }

    private static string NormalizeName(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string CreateBaseSlug(string? slug, string name, string id)
    {
        var explicitSlug = CategorySlug.Normalize(slug);
        if (!string.IsNullOrWhiteSpace(explicitSlug))
        {
            return explicitSlug;
        }

        var nameSlug = CategorySlug.Normalize(name);
        if (!string.IsNullOrWhiteSpace(nameSlug))
        {
            return nameSlug;
        }

        var idSlug = CategorySlug.Normalize(id);
        return string.IsNullOrWhiteSpace(idSlug) ? "category" : idSlug;
    }

    private static string EnsureUniqueSlug(string baseSlug, HashSet<string> usedSlugs)
    {
        var normalized = string.IsNullOrWhiteSpace(baseSlug) ? "category" : baseSlug;
        var candidate = normalized;
        var suffix = 2;
        while (!usedSlugs.Add(candidate))
        {
            candidate = $"{normalized}-{suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            suffix++;
        }

        return candidate;
    }

    private static BuildCategoryNode ToBuildNode(CategoryTreeNode node)
        => new()
        {
            Id = node.Id,
            Name = node.Name,
            Slug = node.Slug,
            Children = node.Children.Select(ToBuildNode).ToArray(),
        };
}

/// <summary>分类树读取结果。</summary>
/// <param name="Scope">分类树所属范围。</param>
/// <param name="Roots">0 层根类别；多个根类别在编辑器中并排展示。</param>
/// <param name="UpdatedAt">最后保存时间；空值表示尚未保存过。</param>
public sealed record CategoryTreeView(
    string Scope,
    IReadOnlyList<CategoryTreeNode> Roots,
    DateTimeOffset? UpdatedAt);

/// <summary>分类树节点。节点 id 负责保持 UI 稳定，slug 负责前台 URL 稳定，名称负责后台展示。</summary>
/// <param name="Id">稳定节点 id。</param>
/// <param name="Name">类别显示名称。</param>
/// <param name="Slug">类别稳定 URL slug。</param>
/// <param name="Children">下一层子类别。</param>
public sealed record CategoryTreeNode(
    string Id,
    string Name,
    string Slug,
    IReadOnlyList<CategoryTreeNode> Children);
