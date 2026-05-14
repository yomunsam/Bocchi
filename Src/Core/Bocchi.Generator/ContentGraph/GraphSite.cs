using Bocchi.ContentModel;

namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 站点全局信息。<see cref="ContentGraph"/> 中的根节点。
/// </summary>
public sealed record GraphSite
{
    /// <summary>原始 <see cref="SiteSettings"/>。任何字段访问都应当通过此属性。</summary>
    public required SiteSettings Settings { get; init; }

    /// <summary>规范化后的站点根 URL，保证以 <c>/</c> 结尾。</summary>
    public required Uri NormalizedBaseUrl { get; init; }
}