namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 派生索引：Builder 一次性算出供下游所有写出器复用。
/// </summary>
/// <param name="LatestPosts">最近发布的 Posts（按 <c>publishedAt desc</c>），数量由 caller 截取。</param>
/// <param name="PostsByYear">按年份分组的 Posts（年降序、内部 <c>publishedAt desc</c>）。</param>
/// <param name="PostsByTag">按标签分组的 Posts；tag key 已 trim。</param>
/// <param name="PostsByCategory">按分类分组的 Posts；无分类的归入 <c>"_uncategorized"</c>。</param>
/// <param name="WorksByYear">按年份分组的 Works。</param>
public sealed record GraphIndices(
    IReadOnlyList<GraphPost> LatestPosts,
    IReadOnlyList<KeyValuePair<string, IReadOnlyList<GraphPost>>> PostsByYear,
    IReadOnlyList<KeyValuePair<string, IReadOnlyList<GraphPost>>> PostsByTag,
    IReadOnlyList<KeyValuePair<string, IReadOnlyList<GraphPost>>> PostsByCategory,
    IReadOnlyList<KeyValuePair<string, IReadOnlyList<GraphWork>>> WorksByYear);
