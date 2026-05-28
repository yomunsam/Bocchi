namespace Bocchi.Generator.ContentGraph;

/// <summary>提供内容图 Post Category tree 的内部遍历 helper。</summary>
internal static class GraphPostCategoryExtensions
{
    /// <summary>按父节点优先顺序展开整棵 Post Category tree，保持输入顺序和子节点顺序不变。</summary>
    internal static IEnumerable<GraphPostCategory> FlattenDepthFirst(this IEnumerable<GraphPostCategory> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in node.Children.FlattenDepthFirst())
            {
                yield return child;
            }
        }
    }
}
