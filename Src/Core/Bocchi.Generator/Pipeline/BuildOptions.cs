namespace Bocchi.Generator.Pipeline;

/// <summary>本次构建的可调参数。</summary>
public sealed record BuildOptions
{
    /// <summary>构建模式。</summary>
    public BuildMode Mode { get; init; } = BuildMode.FullBuild;

    /// <summary>构建环境（development / production / ...）。</summary>
    public string Environment { get; init; } = "production";

    /// <summary>是否纳入草稿。</summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>Feed 最多项数。<c>null</c> 表示沿用 <c>SiteSettings.FeedItemCount</c>。</summary>
    public int? FeedItemCount { get; init; }

    /// <summary>扫描错误是否阻塞构建。</summary>
    public bool FailOnContentError { get; init; } = true;

    /// <summary>在 Live 模式下：仅吐这一个 artifact（如 <c>posts.json</c>）；为空则吐全部。</summary>
    public string? OnlyArtifactPath { get; init; }

    /// <summary>实时模式专用：是否禁用"up-to-date 短路"（默认 Live 下禁用、Full 下启用）。</summary>
    public bool DisableUpToDateShortCircuit { get; init; }

    /// <summary>
    /// Home Server 在触发构建前注入的站点本地化快照。Core Generator 不直接读取 HomeServer 数据库，
    /// 因此测试和 CLI 未提供该快照时会回退到 <c>site.yaml</c> 的单语言事实。
    /// </summary>
    public BuildLocalizationOptions? Localization { get; init; }
}

/// <summary>一次构建使用的站点本地化快照，供 Theme Context 输出使用。</summary>
public sealed record BuildLocalizationOptions
{
    /// <summary>站点主要语言；PrimaryUnprefixed 策略下该语言使用无前缀 URL。</summary>
    public required string PrimaryLanguage { get; init; }

    /// <summary>站点启用语言。调用方应保证包含 <see cref="PrimaryLanguage"/>。</summary>
    public required IReadOnlyList<BuildLanguageRecord> EnabledLanguages { get; init; }

    /// <summary>M6 固定 URL policy；保留在快照里便于后续扩展。</summary>
    public string UrlPolicy { get; init; } = "PrimaryUnprefixed";

    /// <summary>Common i18n key 覆盖，形态为 key -> language -> plain text。</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Text { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>当前 Theme 私有 i18n key 覆盖，形态同 Common 覆盖，并且在同 key 下优先级更高。</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ThemeTextOverrides { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
}

/// <summary>构建快照中的语言描述，不包含图标或地区视觉符号。</summary>
public sealed record BuildLanguageRecord
{
    /// <summary>BCP 47 风格语言代码，例如 <c>en-US</c> 或 <c>zh-CN</c>。</summary>
    public required string Code { get; init; }

    /// <summary>该语言自己的显示名称。</summary>
    public required string NativeName { get; init; }

    /// <summary>该语言的英文显示名称。</summary>
    public required string EnglishName { get; init; }
}
