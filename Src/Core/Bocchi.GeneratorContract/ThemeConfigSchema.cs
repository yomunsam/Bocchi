namespace Bocchi.GeneratorContract;

/// <summary>Theme 配置 schema 字段支持的类型。对应 <c>Docs/Architecture.md §7.4</c>。</summary>
/// <remarks>
/// 枚举值名称（<c>String</c>、<c>Number</c>、<c>Boolean</c> 等）直接对应 Theme Contract 中
/// <c>config-schema.json</c> 的字段 <c>type</c> 取值，不能改名。
/// </remarks>
#pragma warning disable CA1720 // Identifier contains type name — names mandated by Theme Contract v1.
public enum ThemeConfigFieldType
{
    String,
    Number,
    Boolean,
    Select,
    MultiSelect,
    Color,
    Image,
    Url,
    Group,
}
#pragma warning restore CA1720

/// <summary>Theme 配置 schema 中的单个字段。</summary>
public sealed record ThemeConfigField
{
    public required string Key { get; init; }

    public required ThemeConfigFieldType Type { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public object? Default { get; init; }

    public IReadOnlyList<string>? Options { get; init; }
}

/// <summary>Theme 配置 schema 中的字段分组（对应一个设置 Tab）。</summary>
public sealed record ThemeConfigGroup
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<ThemeConfigField> Fields { get; init; } = [];
}

/// <summary>
/// Theme 配置 schema。对应 <c>config-schema.json</c>。
/// </summary>
public sealed record ThemeConfigSchema
{
    public IReadOnlyList<ThemeConfigGroup> Groups { get; init; } = [];
}
