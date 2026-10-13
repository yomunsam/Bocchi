using System.Text.Json;

using Bocchi.ContentModel;
using Bocchi.Generator.ContentGraph;
using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 后台分类树服务。<c>site/categories.yaml</c> 是权威来源，数据库只留投影。
/// 构建快照仍从投影读，但文件不可用时构建会失败，避免拿旧树继续生成。
/// </summary>
public sealed class CategoryTreeService
{
    /// <summary>分类树最大深度：根节点是 0 层，最深子节点是 4 层。</summary>
    public const int MaxDepth = 5;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BocchiDbContext _db;
    private readonly BocchiDataLayout _layout;
    private readonly TimeProvider _time;
    private readonly ILogger<CategoryTreeService> _logger;

    /// <summary>构造分类树服务。</summary>
    public CategoryTreeService(
        BocchiDbContext db,
        BocchiDataLayout layout,
        TimeProvider time,
        ILogger<CategoryTreeService> logger)
    {
        _db = db;
        _layout = layout;
        _time = time;
        _logger = logger;
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

    /// <summary>
    /// 保存指定内容类型的分类树。先写整份 <c>categories.yaml</c>（其他 scope 保留），再更新数据库投影。
    /// 版本过新或现有文件无效时不写文件、不改数据库。
    /// </summary>
    public async Task<WorkspaceWriteResult> SaveAsync(
        ContentKind kind,
        IEnumerable<CategoryTreeNode> roots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var refusal = await PeekSaveRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            _logger.LogError("分类树保存已拒绝：{Message}", refusal.Message);
            return refusal;
        }

        var scope = NormalizeScope(kind);
        var normalizedRoots = NormalizeNodes(roots, depth: 0, new HashSet<string>(StringComparer.Ordinal));
        var document = await CategoryTreeYaml.ReadAsync(_layout.Workspace.CategoriesFile, cancellationToken).ConfigureAwait(false);
        // 闸门和这次读取之间文件可能被改坏。坏文件不能当成空森林，否则会用默认树盖掉它。
        if (document.ErrorCode is not null)
        {
            _logger.LogError("分类树保存已拒绝：{Message}", document.ErrorMessage);
            return WorkspaceWriteResult.Fail(document.ErrorCode, document.ErrorMessage ?? document.ErrorCode);
        }

        var trees = document.IsEmpty
            ? await ExportStoredTreesAsync(cancellationToken).ConfigureAwait(false)
            : document.Trees.Select(tree => new CategoryScopeNodes(tree.Scope, NormalizeStored(tree.Roots))).ToList();
        if (trees.Count == 0)
        {
            trees = DefaultTrees();
        }

        var index = trees.FindIndex(tree => string.Equals(tree.Scope, scope, StringComparison.OrdinalIgnoreCase));
        var updated = new CategoryScopeNodes(scope, normalizedRoots);
        if (index >= 0)
        {
            trees[index] = updated;
        }
        else
        {
            trees.Add(updated);
        }

        await CategoryTreeYaml.WriteAsync(_layout.Workspace.CategoriesFile, trees, cancellationToken).ConfigureAwait(false);
        await ImportTreesAsync(trees, warnOnConflict: false, cancellationToken).ConfigureAwait(false);
        return WorkspaceWriteResult.Ok;
    }

    /// <summary>
    /// 启动同步。文件有效则灌库；文件没有而数据库有数据则导出一次；两边都空则写 Post / Work 空树。
    /// <c>site.yaml</c> 版本过新，或分类文件损坏时，文件和数据库都不动。
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        var refusal = await PeekSaveRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            _logger.LogError("分类树同步已跳过：{Message}", refusal.Message);
            return;
        }

        var document = await CategoryTreeYaml.ReadAsync(_layout.Workspace.CategoriesFile, cancellationToken).ConfigureAwait(false);
        if (document.ErrorCode is not null)
        {
            _logger.LogError("分类树同步已跳过：{Message}", document.ErrorMessage);
            return;
        }

        if (!document.IsEmpty)
        {
            var normalized = document.Trees
                .Select(tree => new CategoryScopeNodes(tree.Scope, NormalizeStored(tree.Roots)))
                .ToList();
            if (document.MissingIds || CanonicalJson(document.Trees) != CanonicalJson(normalized))
            {
                await CategoryTreeYaml.WriteAsync(_layout.Workspace.CategoriesFile, normalized, cancellationToken).ConfigureAwait(false);
            }

            await ImportTreesAsync(normalized, warnOnConflict: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        var stored = await ExportStoredTreesAsync(cancellationToken).ConfigureAwait(false);
        var trees = stored.Count == 0 ? DefaultTrees() : stored;
        await CategoryTreeYaml.WriteAsync(_layout.Workspace.CategoriesFile, trees, cancellationToken).ConfigureAwait(false);
        await ImportTreesAsync(trees, warnOnConflict: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存或构建前的闸门：版本过新，或已有分类文件无效。</summary>
    public async Task<WorkspaceWriteResult?> PeekSaveRefusalAsync(CancellationToken cancellationToken = default)
    {
        var format = await WorkspaceFormatGate.TryRefuseAsync(_layout.Workspace.SiteSettingsFile, cancellationToken).ConfigureAwait(false);
        if (format is not null)
        {
            return format;
        }

        var document = await CategoryTreeYaml.ReadAsync(_layout.Workspace.CategoriesFile, cancellationToken).ConfigureAwait(false);
        return document.ErrorCode is null
            ? null
            : WorkspaceWriteResult.Fail(document.ErrorCode, document.ErrorMessage ?? document.ErrorCode);
    }

    private static string NormalizeScope(ContentKind kind) => kind switch
    {
        ContentKind.Work => nameof(ContentKind.Work),
        _ => nameof(ContentKind.Post),
    };

    /// <summary>读取 Generator 构建所需的 Post Category tree 快照。文件不可用时抛出，避免用旧投影继续构建。</summary>
    public async Task<IReadOnlyList<BuildCategoryNode>> GetBuildPostCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var refusal = await PeekSaveRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            throw new WorkspaceSnapshotException(refusal.Message ?? refusal.Code ?? WorkspaceWriteResult.CategoriesInvalidCode);
        }

        var view = await GetAsync(ContentKind.Post, cancellationToken).ConfigureAwait(false);
        return view.Roots.Select(ToBuildNode).ToArray();
    }

    /// <summary>没有文件也没有数据库记录时的最小文件：Post 和 Work 各一棵空树。</summary>
    private static List<CategoryScopeNodes> DefaultTrees()
        =>
        [
            new(nameof(ContentKind.Post), []),
            new(nameof(ContentKind.Work), []),
        ];

    /// <summary>把数据库投影规范成可写回文件的树。空白 scope 忽略。</summary>
    private async Task<List<CategoryScopeNodes>> ExportStoredTreesAsync(CancellationToken cancellationToken)
    {
        var records = await _db.CategoryTrees.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.Scope))
            .Select(record => new CategoryScopeNodes(record.Scope.Trim(), NormalizeStored(Deserialize(record.TreeJson))))
            .ToList();
    }

    /// <summary>用文件内容覆盖数据库投影。文件里没有的 scope 会从数据库删掉。</summary>
    private async Task ImportTreesAsync(
        IReadOnlyList<CategoryScopeNodes> trees,
        bool warnOnConflict,
        CancellationToken cancellationToken)
    {
        var records = await _db.CategoryTrees.ToListAsync(cancellationToken).ConfigureAwait(false);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var tree in trees)
        {
            keep.Add(tree.Scope);
            var json = JsonSerializer.Serialize(tree.Roots, JsonOptions);
            var record = records.FirstOrDefault(item => string.Equals(item.Scope, tree.Scope, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                _db.CategoryTrees.Add(new CategoryTreeRecord
                {
                    Scope = tree.Scope,
                    TreeJson = json,
                    UpdatedAt = _time.GetUtcNow(),
                });
                changed = true;
                continue;
            }

            if (!string.Equals(CanonicalTreeJson(record.TreeJson), CanonicalTreeJson(json), StringComparison.Ordinal))
            {
                if (warnOnConflict)
                {
                    _logger.LogWarning("分类树 scope {Scope} 与数据库不一致，以 categories.yaml 为准。", tree.Scope);
                }

                record.Scope = tree.Scope;
                record.TreeJson = json;
                record.UpdatedAt = _time.GetUtcNow();
                changed = true;
            }
        }

        foreach (var extra in records.Where(record => !keep.Contains(record.Scope)))
        {
            _db.CategoryTrees.Remove(extra);
            changed = true;
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>把已存节点再走一遍规范化，保证写回文件的 id、slug 和深度与后台保存一致。</summary>
    private static List<CategoryTreeNode> NormalizeStored(IReadOnlyList<CategoryTreeNode> roots)
        => NormalizeNodes(roots, depth: 0, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>把树序列化成稳定 JSON，用来判断要不要把补过 id 的结果写回文件。</summary>
    private static string CanonicalJson(IEnumerable<CategoryScopeNodes> trees)
        => JsonSerializer.Serialize(CategoryTreeYaml.Order(trees).Select(tree => new { tree.Scope, tree.Roots }), JsonOptions);

    /// <summary>把数据库里的树 JSON 再序列化一次，避免键顺序造成假冲突。损坏的 JSON 保持原样，从而触发按文件覆盖。</summary>
    private static string CanonicalTreeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Serialize(Array.Empty<CategoryTreeNode>(), JsonOptions);
        }

        try
        {
            var nodes = JsonSerializer.Deserialize<List<CategoryTreeNode>>(json, JsonOptions) ?? [];
            return JsonSerializer.Serialize(nodes, JsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
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
        var localizedNames = NormalizeLocalizedNames(node.LocalizedNames);
        return new CategoryTreeNode(id, name, slug, children)
        {
            LocalizedNames = localizedNames,
        };
    }

    /// <summary>清洗多语言名称：去空白、丢空键、值 Trim；全空时返回 null 以保持 JSON 体积。</summary>
    private static Dictionary<string, string>? NormalizeLocalizedNames(IReadOnlyDictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var trimmed = pair.Value?.Trim() ?? string.Empty;
            result[pair.Key.Trim()] = trimmed;
        }

        return result.Count == 0 ? null : result;
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
            LocalizedNames = node.LocalizedNames,
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
/// <param name="Name">类别显示名称（主语言或回退名）。</param>
/// <param name="Slug">类别稳定 URL slug。</param>
/// <param name="Children">下一层子类别。</param>
public sealed record CategoryTreeNode(
    string Id,
    string Name,
    string Slug,
    IReadOnlyList<CategoryTreeNode> Children)
{
    /// <summary>多语言显示名覆盖（langCode → 显示名）；未配置时为空，前端按当前语言回退到 <see cref="Name"/>。</summary>
    public IReadOnlyDictionary<string, string>? LocalizedNames { get; init; }
}
