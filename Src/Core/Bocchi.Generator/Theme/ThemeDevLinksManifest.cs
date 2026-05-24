namespace Bocchi.Generator.Theme;

/// <summary><c>&lt;data&gt;/themes/dev-links.json</c> 的根对象。</summary>
public sealed record ThemeDevLinksManifest
{
    /// <summary>Dev Link manifest schema 版本。</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>开发期外部 Theme Root 映射列表。</summary>
    public IReadOnlyList<ThemeDevLinkEntry> Links { get; init; } = [];
}

/// <summary>单个 Dev Link 映射项。</summary>
public sealed record ThemeDevLinkEntry
{
    /// <summary>映射的 Theme id，必须与外部 Theme Root 的 <c>theme.json.id</c> 一致。</summary>
    public required string Id { get; init; }

    /// <summary>外部 Theme Root 绝对路径；Docker 下应使用容器内挂载路径。</summary>
    public required string Root { get; init; }

    /// <summary>是否参与 Theme 解析；禁用项只保留在清单中。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>给 Theme 作者自己的备注，不参与解析。</summary>
    public string? Note { get; init; }
}
