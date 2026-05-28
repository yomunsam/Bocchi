using Bocchi.GeneratorContract;
using Bocchi.Generator.Theme;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Dashboard 主题定制页的完整视图模型，聚合当前前台 Theme、schema 字段和已保存配置值。
/// </summary>
public sealed class ThemeCustomizationView
{
    /// <summary>当前正在使用的前台 Theme id。</summary>
    public required string ThemeId { get; init; }

    /// <summary>当前前台 Theme 的展示名称；manifest 不可用时回退到 Theme id。</summary>
    public required string ThemeName { get; init; }

    /// <summary>当前 Theme 版本；manifest 不可用时为空。</summary>
    public string? Version { get; init; }

    /// <summary>当前 Theme Contract 版本；manifest 不可用时为空。</summary>
    public string? ContractVersion { get; init; }

    /// <summary>当前 Theme Root；manifest 不可用时为空。</summary>
    public string? ThemeRoot { get; init; }

    /// <summary>当前 Theme 来源；manifest 不可用时为空。</summary>
    public ThemeSourceKind? SourceKind { get; init; }

    /// <summary>当前 Theme runner 类型；manifest 不可用时为空。</summary>
    public string? RunnerKind { get; init; }

    /// <summary>当前 Theme 解析诊断；Dashboard 用它展示 Dev Link shadow 等状态。</summary>
    public IReadOnlyList<ThemeDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>当前 Theme 原始配置 JSON，主要用于调试和后续高级视图。</summary>
    public required string ConfigurationJson { get; init; }

    /// <summary>Theme <c>config-schema.json</c> 声明的配置分组。</summary>
    public required IReadOnlyList<ThemeConfigGroupView> Groups { get; init; }
}

/// <summary>Theme 配置 schema 的 Dashboard 分组视图。</summary>
public sealed class ThemeConfigGroupView
{
    /// <summary>分组标识，来自 <c>config-schema.json</c>。</summary>
    public required string Id { get; init; }

    /// <summary>分组标题，直接展示给 Admin 用户。</summary>
    public required string Title { get; init; }

    /// <summary>分组内可编辑的 Theme 配置字段。</summary>
    public required IReadOnlyList<ThemeConfigFieldView> Fields { get; init; }
}

/// <summary>Theme 配置 schema 的单字段 Dashboard 视图。</summary>
public sealed class ThemeConfigFieldView
{
    /// <summary>字段 key，支持点分路径，例如 <c>home.featuredPosts</c>。</summary>
    public required string Key { get; init; }

    /// <summary>字段类型，决定 Dashboard 使用的输入控件。</summary>
    public required ThemeConfigFieldType Type { get; init; }

    /// <summary>字段标题，来自 Theme schema。</summary>
    public required string Title { get; init; }

    /// <summary>字段说明，来自 Theme schema。</summary>
    public string? Description { get; init; }

    /// <summary>文本字段的可选表现层格式；plain 表示 Dashboard 只按普通文本保存。</summary>
    public string TextFormat { get; init; } = "plain";

    /// <summary>输入框占位提示，来自 Theme schema。</summary>
    public string? Placeholder { get; init; }

    /// <summary>字段帮助文本，来自 Theme schema。</summary>
    public string? HelpText { get; init; }

    /// <summary>字段是否必填，当前用于展示语义，保存逻辑仍保持 Theme 默认值可回退。</summary>
    public bool Required { get; init; }

    /// <summary>Select 和 MultiSelect 可用选项；Value 用于保存，Label 用于展示。</summary>
    public required IReadOnlyList<ThemeConfigOptionView> Options { get; init; }

    /// <summary>当前有效文本值；没有用户配置时使用 schema 默认值。</summary>
    public required string TextValue { get; init; }

    /// <summary>当前有效布尔值；仅 Boolean 字段使用。</summary>
    public bool BooleanValue { get; init; }

    /// <summary>当前有效多选值；仅 MultiSelect 字段使用。</summary>
    public required IReadOnlyList<string> SelectedValues { get; init; }

    /// <summary>当前保存的多语言文本值；仅 LocalizedText 字段使用，空语言表示回退默认值。</summary>
    public required IReadOnlyDictionary<string, string> LocalizedTextValues { get; init; }

    /// <summary>schema 默认多语言文本值；仅 LocalizedText 字段用于输入框占位提示。</summary>
    public required IReadOnlyDictionary<string, string> DefaultLocalizedTextValues { get; init; }

    /// <summary>当前保存的多语言文本列表；仅 LocalizedTextList 字段使用，空语言表示回退默认值。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> LocalizedTextListValues { get; init; }

    /// <summary>schema 默认多语言文本列表；仅 LocalizedTextList 字段用于输入框占位提示。</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultLocalizedTextListValues { get; init; }

    /// <summary>schema 默认值的文本表示，用于占位提示和只读辅助信息。</summary>
    public string? DefaultText { get; init; }
}

/// <summary>Theme 配置字段的选项视图，兼容 schema 中的字符串选项和 value/label 对象。</summary>
public sealed class ThemeConfigOptionView
{
    /// <summary>写入 Theme 配置 JSON 的稳定值。</summary>
    public required string Value { get; init; }

    /// <summary>展示给 Dashboard 用户看的标签；缺失时与 Value 相同。</summary>
    public required string Label { get; init; }
}

/// <summary>主题定制页提交给服务层的单个字段值。</summary>
public sealed class ThemeConfigValueInput
{
    /// <summary>字段 key，必须来自当前 Theme schema。</summary>
    public required string Key { get; init; }

    /// <summary>单值字段的输入值；Boolean 和 Number 也使用字符串承载。</summary>
    public string? Value { get; init; }

    /// <summary>MultiSelect 字段的输入值集合。</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>LocalizedText 字段的语言值集合，key 为语言代码。</summary>
    public IReadOnlyDictionary<string, string> LocalizedValues { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>LocalizedTextList 字段的语言值集合，key 为语言代码。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> LocalizedListValues { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
