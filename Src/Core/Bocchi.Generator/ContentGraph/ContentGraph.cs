namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 站点视角的标准化、不可变内容图。由 <see cref="ContentGraphBuilder"/> 一次性构造。
/// </summary>
public sealed record ContentGraph
{
    public required GraphSite Site { get; init; }

    public IReadOnlyList<GraphPost> Posts { get; init; } = [];

    public IReadOnlyList<GraphPage> Pages { get; init; } = [];

    public IReadOnlyList<GraphWork> Works { get; init; } = [];

    public IReadOnlyList<GraphNote> Notes { get; init; } = [];

    public IReadOnlyList<GraphFriend> Friends { get; init; } = [];

    public IReadOnlyList<MediaAsset> MediaAssets { get; init; } = [];

    public required GraphIndices Indices { get; init; }
}
