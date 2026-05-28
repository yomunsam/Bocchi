using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bocchi.GeneratorContract;

/// <summary>Theme 配置 schema 字段支持的类型。对应 <c>Docs/Architecture.md §7.4</c>。</summary>
/// <remarks>
/// 枚举值名称（<c>String</c>、<c>Number</c>、<c>Boolean</c> 等）直接对应 Theme Contract 中
/// <c>config-schema.json</c> 的字段 <c>type</c> 取值，不能改名。
/// </remarks>
#pragma warning disable CA1720 // Identifier contains type name — names mandated by Theme Contract v1.
[JsonConverter(typeof(JsonStringEnumConverter<ThemeConfigFieldType>))]
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
    LocalizedText,
    LocalizedTextList,
    Group,
}
#pragma warning restore CA1720

/// <summary>Theme 配置 schema 中的单个字段。</summary>
public sealed record ThemeConfigField
{
    /// <summary>字段 key，支持点分路径，例如 <c>reading.showUpdatedAt</c>。</summary>
    public required string Key { get; init; }

    /// <summary>字段类型，决定 Dashboard 使用的输入控件。</summary>
    public required ThemeConfigFieldType Type { get; init; }

    /// <summary>字段标题。</summary>
    public required string Title { get; init; }

    /// <summary>字段说明。</summary>
    public string? Description { get; init; }

    /// <summary>文本字段的可选表现层格式，例如 <c>inlineColor</c>；未声明时按 plain text 处理。</summary>
    public string? TextFormat { get; init; }

    /// <summary>字段默认值，类型由字段声明决定。</summary>
    public object? Default { get; init; }

    /// <summary>Select 和 MultiSelect 字段的可选项；兼容旧版字符串数组和新版 value/label 对象。</summary>
    public IReadOnlyList<ThemeConfigOption>? Options { get; init; }
}

/// <summary>Theme 配置字段的可选项，Value 用于保存，Label 用于 Dashboard 展示。</summary>
[JsonConverter(typeof(ThemeConfigOptionJsonConverter))]
public sealed record ThemeConfigOption
{
    /// <summary>写入 Theme 配置 JSON 的稳定值。</summary>
    public required string Value { get; init; }

    /// <summary>展示给用户看的标签；缺失时回退到 Value。</summary>
    public string? Label { get; init; }
}

/// <summary>Theme 配置 schema 中的字段分组（对应一个设置 Tab）。</summary>
public sealed record ThemeConfigGroup
{
    /// <summary>分组标识。</summary>
    public required string Id { get; init; }

    /// <summary>分组标题。</summary>
    public required string Title { get; init; }

    /// <summary>分组内字段声明。</summary>
    public IReadOnlyList<ThemeConfigField> Fields { get; init; } = [];
}

/// <summary>
/// Theme 配置 schema。对应 <c>config-schema.json</c>。
/// </summary>
public sealed record ThemeConfigSchema
{
    /// <summary>Theme 配置字段分组。</summary>
    public IReadOnlyList<ThemeConfigGroup> Groups { get; init; } = [];
}

/// <summary>兼容读取旧版字符串 option 和新版 value/label option。</summary>
internal sealed class ThemeConfigOptionJsonConverter : JsonConverter<ThemeConfigOption>
{
    /// <inheritdoc />
    public override ThemeConfigOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString() ?? string.Empty;
            return new ThemeConfigOption
            {
                Value = value,
                Label = value,
            };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Theme config option must be a string or an object.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var valueText = ReadString(root, "value");
        if (string.IsNullOrWhiteSpace(valueText))
        {
            throw new JsonException("Theme config option object must contain a non-empty value.");
        }

        return new ThemeConfigOption
        {
            Value = valueText.Trim(),
            Label = ReadString(root, "label")?.Trim(),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ThemeConfigOption value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("value", value.Value);
        if (!string.IsNullOrWhiteSpace(value.Label))
        {
            writer.WriteString("label", value.Label);
        }

        writer.WriteEndObject();
    }

    /// <summary>安全读取 option 对象中的字符串字段。</summary>
    private static string? ReadString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
