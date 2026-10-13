using System.Text.Json;

using Bocchi.ContentModel;
using Bocchi.Generator.ContentGraph;
using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 后台分类树服务。workspace 的 <c>site/categories.yaml</c> 为权威；数据库保留投影。
/// 形状世代跟 <c>site.yaml</c> 的 formatVersion。
/// </summary>
public sealed class CategoryTreeService
{
    /// <summary>分类树最大深度：根节点是 0 层，最深子节点是 4 层。</summary>
    public const int MaxDepth = 5;

    private static readonly string[] KnownScopes =
    [
        nameof(ContentKind.Post),
        nameof(ContentKind.Work),
    ];

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
    /// 启动同步：文件→DB；无文件且 DB 有数据则导出一次；两边都空则写最小默认。
    /// 坏文件不灌库、不覆盖；过新 site.yaml formatVersion 拒绝。
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        WorkspaceFormatVersion.EnsureExistingFileAllowsWrite(_layout.Workspace.SiteSettingsFile);

        var file = _layout.Workspace.CategoriesFile;
        if (File.Exists(file) && new FileInfo(file).Length > 0)
        {
            if (!TryLoadTreesFromFile(file, out var trees, out var error))
            {
                _logger.LogError("categories.yaml 无效，跳过灌库，不覆盖文件：{Error}", error);
                return;
            }

            await ImportTreesIntoDbAsync(trees, cancellationToken).ConfigureAwait(false);
            return;
        }

        var fromDb = await LoadAllTreesFromDbAsync(cancellationToken).ConfigureAwait(false);
        if (fromDb.Any(t => t.Roots.Count > 0))
        {
            await WriteCategoriesYamlAsync(fromDb, cancellationToken).ConfigureAwait(false);
            return;
        }

        var defaults = CreateDefaultTrees();
        await WriteCategoriesYamlAsync(defaults, cancellationToken).ConfigureAwait(false);
        await ImportTreesIntoDbAsync(defaults, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存指定内容类型的分类树：先写 YAML，再写 DB。</summary>
    public async Task SaveAsync(
        ContentKind kind,
        IEnumerable<CategoryTreeNode> roots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        WorkspaceFormatVersion.EnsureExistingFileAllowsWrite(_layout.Workspace.SiteSettingsFile);

        var scope = NormalizeScope(kind);
        var usedSlugs = new HashSet<string>(StringComparer.Ordinal);
        var normalizedRoots = NormalizeNodes(roots, depth: 0, usedSlugs);

        var trees = await LoadAllTreesFromDbAsync(cancellationToken).ConfigureAwait(false);
        var replaced = false;
        for (var i = 0; i < trees.Count; i++)
        {
            if (string.Equals(trees[i].Scope, scope, StringComparison.Ordinal))
            {
                trees[i] = new ScopeTree(scope, normalizedRoots);
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            trees.Add(new ScopeTree(scope, normalizedRoots));
        }

        EnsureKnownScopesPresent(trees);
        await WriteCategoriesYamlAsync(trees, cancellationToken).ConfigureAwait(false);
        await UpsertScopeInDbAsync(scope, normalizedRoots, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取 Generator 构建所需的 Post Category tree 快照。</summary>
    public async Task<IReadOnlyList<BuildCategoryNode>> GetBuildPostCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var view = await GetAsync(ContentKind.Post, cancellationToken).ConfigureAwait(false);
        return view.Roots.Select(ToBuildNode).ToArray();
    }

    private async Task ImportTreesIntoDbAsync(IReadOnlyList<ScopeTree> trees, CancellationToken cancellationToken)
    {
        foreach (var tree in trees)
        {
            await UpsertScopeInDbAsync(tree.Scope, tree.Roots, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpsertScopeInDbAsync(
        string scope,
        IReadOnlyList<CategoryTreeNode> roots,
        CancellationToken cancellationToken)
    {
        var record = await _db.CategoryTrees
            .FirstOrDefaultAsync(x => x.Scope == scope, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new CategoryTreeRecord { Scope = scope };
            _db.CategoryTrees.Add(record);
        }

        record.TreeJson = JsonSerializer.Serialize(roots, JsonOptions);
        record.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<ScopeTree>> LoadAllTreesFromDbAsync(CancellationToken cancellationToken)
    {
        var records = await _db.CategoryTrees
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byScope = records.ToDictionary(r => r.Scope, r => Deserialize(r.TreeJson), StringComparer.Ordinal);
        var result = new List<ScopeTree>();
        foreach (var scope in KnownScopes)
        {
            result.Add(new ScopeTree(scope, byScope.TryGetValue(scope, out var roots) ? roots : []));
        }

        foreach (var pair in byScope)
        {
            if (!KnownScopes.Contains(pair.Key, StringComparer.Ordinal))
            {
                result.Add(new ScopeTree(pair.Key, pair.Value));
            }
        }

        return result;
    }

    private async Task WriteCategoriesYamlAsync(IReadOnlyList<ScopeTree> trees, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_layout.Workspace.SiteDirectory);
        var root = new YamlMappingNode
        {
            { new YamlScalarNode("trees"), BuildTreesYaml(trees) },
        };
        var stream = new YamlStream(new YamlDocument(root));
        await using var output = File.Create(_layout.Workspace.CategoriesFile);
        await using var writer = new StreamWriter(output);
        stream.Save(writer, assignAnchors: false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static YamlSequenceNode BuildTreesYaml(IReadOnlyList<ScopeTree> trees)
    {
        var seq = new YamlSequenceNode();
        foreach (var tree in trees)
        {
            seq.Add(new YamlMappingNode
            {
                { new YamlScalarNode("scope"), new YamlScalarNode(tree.Scope) },
                { new YamlScalarNode("roots"), BuildNodesYaml(tree.Roots) },
            });
        }

        return seq;
    }

    private static YamlSequenceNode BuildNodesYaml(IReadOnlyList<CategoryTreeNode> nodes)
    {
        var seq = new YamlSequenceNode();
        foreach (var node in nodes)
        {
            var map = new YamlMappingNode
            {
                { new YamlScalarNode("id"), new YamlScalarNode(node.Id) },
                { new YamlScalarNode("name"), new YamlScalarNode(node.Name) },
                { new YamlScalarNode("slug"), new YamlScalarNode(node.Slug) },
                { new YamlScalarNode("children"), BuildNodesYaml(node.Children) },
            };
            if (node.LocalizedNames is { Count: > 0 })
            {
                var loc = new YamlMappingNode();
                foreach (var pair in node.LocalizedNames.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    loc.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));
                }

                map.Add(new YamlScalarNode("localizedNames"), loc);
            }

            seq.Add(map);
        }

        return seq;
    }

    private static bool TryLoadTreesFromFile(string file, out List<ScopeTree> trees, out string error)
    {
        trees = [];
        error = string.Empty;
        string raw;
        try
        {
            raw = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "文件为空。";
            return false;
        }

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(raw);
            stream.Load(reader);
            if (stream.Documents.Count == 0
                || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                error = "根节点必须是 mapping，且包含 trees。";
                return false;
            }

            if (!TryGetSequence(root, "trees", out var treesSeq) || treesSeq is null)
            {
                error = "缺少 trees 序列。";
                return false;
            }

            var seenScopes = new HashSet<string>(StringComparer.Ordinal);
            var parsed = new List<ScopeTree>();
            foreach (var child in treesSeq.Children)
            {
                if (child is not YamlMappingNode map)
                {
                    error = "trees 项必须是 mapping。";
                    return false;
                }

                var scope = ReadScalar(map, "scope")?.Trim();
                if (string.IsNullOrWhiteSpace(scope))
                {
                    error = "trees 项缺少 scope。";
                    return false;
                }

                if (!seenScopes.Add(scope))
                {
                    error = $"scope '{scope}' 重复。";
                    return false;
                }

                var rootsSeq = TryGetSequence(map, "roots", out var rs) ? rs : new YamlSequenceNode();
                var usedSlugs = new HashSet<string>(StringComparer.Ordinal);
                var roots = ParseNodes(rootsSeq!, depth: 0, usedSlugs);
                parsed.Add(new ScopeTree(scope, roots));
            }

            EnsureKnownScopesPresent(parsed);
            trees = parsed;
            return true;
        }
        catch (YamlException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void EnsureKnownScopesPresent(List<ScopeTree> trees)
    {
        foreach (var scope in KnownScopes)
        {
            if (!trees.Any(t => string.Equals(t.Scope, scope, StringComparison.Ordinal)))
            {
                trees.Add(new ScopeTree(scope, []));
            }
        }
    }

    private static List<ScopeTree> CreateDefaultTrees()
        => KnownScopes.Select(scope => new ScopeTree(scope, [])).ToList();

    private static List<CategoryTreeNode> ParseNodes(YamlSequenceNode seq, int depth, HashSet<string> usedSlugs)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }

        var raw = new List<CategoryTreeNode>();
        foreach (var child in seq.Children)
        {
            if (child is not YamlMappingNode map)
            {
                continue;
            }

            var name = ReadScalar(map, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = ReadScalar(map, "id");
            var slug = ReadScalar(map, "slug");
            var childrenSeq = TryGetSequence(map, "children", out var cs) ? cs : new YamlSequenceNode();
            var localized = ReadLocalizedNames(map);
            // 子节点先按原文组装，统一交给 NormalizeNodes，避免 slug 去重跑两遍。
            raw.Add(new CategoryTreeNode(
                string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                name.Trim(),
                slug?.Trim() ?? string.Empty,
                ParseRawChildren(childrenSeq!, depth + 1))
            {
                LocalizedNames = localized,
            });
        }

        return NormalizeNodes(raw, depth, usedSlugs);
    }

    private static List<CategoryTreeNode> ParseRawChildren(YamlSequenceNode seq, int depth)
    {
        if (depth >= MaxDepth)
        {
            return [];
        }

        var result = new List<CategoryTreeNode>();
        foreach (var child in seq.Children)
        {
            if (child is not YamlMappingNode map)
            {
                continue;
            }

            var name = ReadScalar(map, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var id = ReadScalar(map, "id");
            var slug = ReadScalar(map, "slug");
            var childrenSeq = TryGetSequence(map, "children", out var cs) ? cs : new YamlSequenceNode();
            result.Add(new CategoryTreeNode(
                string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim(),
                name.Trim(),
                slug?.Trim() ?? string.Empty,
                ParseRawChildren(childrenSeq!, depth + 1))
            {
                LocalizedNames = ReadLocalizedNames(map),
            });
        }

        return result;
    }

    private static Dictionary<string, string>? ReadLocalizedNames(YamlMappingNode map)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode("localizedNames"), out var node)
            || node is not YamlMappingNode loc)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keyNode, valueNode) in loc.Children)
        {
            if (keyNode is not YamlScalarNode key || valueNode is not YamlScalarNode value
                || string.IsNullOrWhiteSpace(key.Value))
            {
                continue;
            }

            result[key.Value.Trim()] = value.Value?.Trim() ?? string.Empty;
        }

        return result.Count == 0 ? null : result;
    }

    private static string NormalizeScope(ContentKind kind) => kind switch
    {
        ContentKind.Work => nameof(ContentKind.Work),
        _ => nameof(ContentKind.Post),
    };

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

    private static string? ReadScalar(YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    private static bool TryGetSequence(YamlMappingNode map, string key, out YamlSequenceNode? seq)
    {
        seq = null;
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return false;
        }

        if (node is YamlSequenceNode sequence)
        {
            seq = sequence;
            return true;
        }

        return false;
    }

    private sealed record ScopeTree(string Scope, IReadOnlyList<CategoryTreeNode> Roots);
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
