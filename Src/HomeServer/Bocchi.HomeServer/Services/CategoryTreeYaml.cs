using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>一棵分类树在 YAML 里的样子：scope 加上尚未规范化的根节点。</summary>
internal sealed record CategoryScopeNodes(string Scope, IReadOnlyList<CategoryTreeNode> Roots);

/// <summary>
/// <c>site/categories.yaml</c> 的读取结果。
/// <see cref="IsEmpty"/> 表示文件不存在或空白，调用方可以按数据库导出。
/// </summary>
internal sealed record CategoryTreeDocument(
    bool IsEmpty,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<CategoryScopeNodes> Trees,
    bool MissingIds)
{
    /// <summary>空文件，或还没有这个文件。</summary>
    public static CategoryTreeDocument Empty { get; } = new(true, null, null, [], false);

    /// <summary>schema 或 YAML 不通过。调用方不得灌库，也不得覆盖文件。</summary>
    public static CategoryTreeDocument Invalid(string message)
        => new(false, WorkspaceWriteResult.CategoriesInvalidCode, message, [], false);
}

/// <summary>
/// 读写 <c>site/categories.yaml</c>。顶层不写 <c>formatVersion</c>，世代跟 <c>site.yaml</c>。
/// 同一 scope 出现两次直接判失败，不取最后一次。
/// </summary>
internal static class CategoryTreeYaml
{
    private const string Header = "# 分类树。scope 与后台一致（Post / Work）。根为 0 层，深度上限 5。";

    /// <summary>读文件。空白文件视为空，坏结构和重复 scope 视为无效。</summary>
    public static async Task<CategoryTreeDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var (root, error) = await WorkspaceYaml.LoadMappingAsync(path, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return CategoryTreeDocument.Invalid($"categories.yaml {error}");
        }

        if (root is null)
        {
            return CategoryTreeDocument.Empty;
        }

        var trees = WorkspaceYaml.ReadSequence(root, "trees", out var treesTypeError);
        if (treesTypeError || trees is null)
        {
            return CategoryTreeDocument.Invalid("categories.yaml 必须包含 trees 序列。");
        }

        var parsed = new List<CategoryScopeNodes>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingIds = false;
        foreach (var child in trees.Children)
        {
            if (child is not YamlMappingNode tree)
            {
                return CategoryTreeDocument.Invalid("categories.yaml 的 trees 项必须是 mapping。");
            }

            var scope = WorkspaceYaml.ReadScalar(tree, "scope")?.Trim();
            if (string.IsNullOrWhiteSpace(scope))
            {
                return CategoryTreeDocument.Invalid("categories.yaml 的每棵树都要有 scope。");
            }

            if (!seen.Add(scope))
            {
                return CategoryTreeDocument.Invalid($"categories.yaml 里 scope「{scope}」出现了多次。同一 scope 只能有一棵树。");
            }

            var roots = WorkspaceYaml.ReadSequence(tree, "roots", out var rootsTypeError);
            if (rootsTypeError || roots is null)
            {
                return CategoryTreeDocument.Invalid($"categories.yaml 的 scope「{scope}」必须包含 roots 序列。");
            }

            var nodes = ReadNodes(roots, ref missingIds, out var nodeError);
            if (nodeError is not null)
            {
                return CategoryTreeDocument.Invalid(nodeError);
            }

            parsed.Add(new CategoryScopeNodes(scope, nodes));
        }

        return new CategoryTreeDocument(false, null, null, parsed, missingIds);
    }

    /// <summary>按稳定顺序写出全部 scope。不写 <c>UpdatedAt</c>。</summary>
    public static Task WriteAsync(string path, IReadOnlyList<CategoryScopeNodes> trees, CancellationToken cancellationToken)
    {
        var sequence = new YamlSequenceNode();
        foreach (var tree in Order(trees))
        {
            sequence.Add(new YamlMappingNode
            {
                { new YamlScalarNode("scope"), new YamlScalarNode(tree.Scope) },
                { new YamlScalarNode("roots"), WriteNodes(tree.Roots) },
            });
        }

        var root = new YamlMappingNode
        {
            { new YamlScalarNode("trees"), sequence },
        };
        return WorkspaceYaml.WriteAsync(path, root, Header, cancellationToken);
    }

    /// <summary>Post、Work 在前，其余 scope 按字符串排序，避免每次导出都换顺序。</summary>
    public static IEnumerable<CategoryScopeNodes> Order(IEnumerable<CategoryScopeNodes> trees)
        => trees
            .OrderBy(tree => ScopeRank(tree.Scope))
            .ThenBy(tree => tree.Scope, StringComparer.OrdinalIgnoreCase);

    private static int ScopeRank(string scope)
        => scope.Equals("Post", StringComparison.OrdinalIgnoreCase) ? 0
            : scope.Equals("Work", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

    private static List<CategoryTreeNode> ReadNodes(YamlSequenceNode sequence, ref bool missingIds, out string? error)
    {
        var nodes = new List<CategoryTreeNode>();
        foreach (var child in sequence.Children)
        {
            if (child is not YamlMappingNode map)
            {
                error = "categories.yaml 的类别节点必须是 mapping。";
                return [];
            }

            var id = WorkspaceYaml.ReadScalar(map, "id")?.Trim() ?? string.Empty;
            if (id.Length == 0)
            {
                missingIds = true;
            }

            var name = WorkspaceYaml.ReadScalar(map, "name") ?? string.Empty;
            var slug = WorkspaceYaml.ReadScalar(map, "slug") ?? string.Empty;
            var localized = ReadLocalizedNames(map, out var namesError);
            if (namesError is not null)
            {
                error = namesError;
                return [];
            }

            IReadOnlyList<CategoryTreeNode> children = [];
            if (map.Children.ContainsKey(new YamlScalarNode("children")))
            {
                var childSeq = WorkspaceYaml.ReadSequence(map, "children", out var childTypeError);
                if (childTypeError || childSeq is null)
                {
                    error = "categories.yaml 的 children 必须是序列。";
                    return [];
                }

                children = ReadNodes(childSeq, ref missingIds, out var childError);
                if (childError is not null)
                {
                    error = childError;
                    return [];
                }
            }

            nodes.Add(new CategoryTreeNode(id, name, slug, children)
            {
                LocalizedNames = localized,
            });
        }

        error = null;
        return nodes;
    }

    private static Dictionary<string, string>? ReadLocalizedNames(YamlMappingNode map, out string? error)
    {
        error = null;
        if (!map.Children.ContainsKey(new YamlScalarNode("localizedNames")))
        {
            return null;
        }

        var names = WorkspaceYaml.ReadMapping(map, "localizedNames");
        if (names is null)
        {
            error = "categories.yaml 的 localizedNames 必须是 mapping。";
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in names.Children)
        {
            if (pair.Key is not YamlScalarNode key || pair.Value is not YamlScalarNode value)
            {
                error = "categories.yaml 的 localizedNames 只能是字符串到字符串。";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(key.Value))
            {
                result[key.Value.Trim()] = value.Value?.Trim() ?? string.Empty;
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static YamlSequenceNode WriteNodes(IReadOnlyList<CategoryTreeNode> nodes)
    {
        var sequence = new YamlSequenceNode();
        foreach (var node in nodes)
        {
            var map = new YamlMappingNode
            {
                { new YamlScalarNode("id"), new YamlScalarNode(node.Id) },
                { new YamlScalarNode("name"), new YamlScalarNode(node.Name) },
                { new YamlScalarNode("slug"), new YamlScalarNode(node.Slug) },
            };
            if (node.LocalizedNames is { Count: > 0 })
            {
                var names = new YamlMappingNode();
                foreach (var pair in node.LocalizedNames.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));
                }

                map.Add(new YamlScalarNode("localizedNames"), names);
            }

            map.Add(new YamlScalarNode("children"), WriteNodes(node.Children));
            sequence.Add(map);
        }

        return sequence;
    }
}
