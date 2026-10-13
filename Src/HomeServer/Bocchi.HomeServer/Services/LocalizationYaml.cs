using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// <c>site/localization.yaml</c> 的读取结果。空白文件视为不存在。
/// 这里只做 schema 校验，语言是否补进 enabled 由服务层规范化时决定。
/// </summary>
internal sealed record LocalizationYamlDocument(
    bool IsEmpty,
    string? ErrorCode,
    string? ErrorMessage,
    string? PrimaryLanguage,
    string? UrlPolicy,
    IReadOnlyList<LanguageRecord> EnabledLanguages,
    IReadOnlyList<LanguageRecord> CustomLanguages,
    IReadOnlyList<CommonI18nTextOverride> CommonTextOverrides)
{
    /// <summary>空文件，或还没有这个文件。</summary>
    public static LocalizationYamlDocument Empty { get; } = new(true, null, null, null, null, [], [], []);

    /// <summary>schema 或 YAML 不通过。调用方不得灌库，也不得覆盖文件。</summary>
    public static LocalizationYamlDocument Invalid(string message)
        => new(false, WorkspaceWriteResult.LocalizationInvalidCode, message, null, null, [], [], []);
}

/// <summary>
/// 读写 <c>site/localization.yaml</c>。顶层不写 <c>formatVersion</c>，也不写 <c>UpdatedAt</c>。
/// </summary>
internal static class LocalizationYaml
{
    private const string Header = "# 前台语言。后台界面语言不在这个文件。";

    /// <summary>读文件。缺 <c>primaryLanguage</c> 或 <c>enabledLanguages</c> 视为无效。</summary>
    public static async Task<LocalizationYamlDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var (root, error) = await WorkspaceYaml.LoadMappingAsync(path, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return LocalizationYamlDocument.Invalid($"localization.yaml {error}");
        }

        if (root is null)
        {
            return LocalizationYamlDocument.Empty;
        }

        var primary = WorkspaceYaml.ReadScalar(root, "primaryLanguage")?.Trim();
        if (string.IsNullOrWhiteSpace(primary))
        {
            return LocalizationYamlDocument.Invalid("localization.yaml 必须包含 primaryLanguage。");
        }

        var enabledSeq = WorkspaceYaml.ReadSequence(root, "enabledLanguages", out var enabledTypeError);
        if (enabledTypeError || enabledSeq is null)
        {
            return LocalizationYamlDocument.Invalid("localization.yaml 必须包含 enabledLanguages 序列。");
        }

        var enabled = ReadLanguages(enabledSeq, "enabledLanguages", out var enabledError);
        if (enabledError is not null)
        {
            return LocalizationYamlDocument.Invalid(enabledError);
        }

        IReadOnlyList<LanguageRecord> custom = [];
        if (root.Children.ContainsKey(new YamlScalarNode("customLanguages")))
        {
            var customSeq = WorkspaceYaml.ReadSequence(root, "customLanguages", out var customTypeError);
            if (customTypeError || customSeq is null)
            {
                return LocalizationYamlDocument.Invalid("localization.yaml 的 customLanguages 必须是序列。");
            }

            custom = ReadLanguages(customSeq, "customLanguages", out var customError);
            if (customError is not null)
            {
                return LocalizationYamlDocument.Invalid(customError);
            }
        }

        IReadOnlyList<CommonI18nTextOverride> overrides = [];
        if (root.Children.ContainsKey(new YamlScalarNode("commonTextOverrides")))
        {
            var overrideMap = WorkspaceYaml.ReadMapping(root, "commonTextOverrides");
            if (overrideMap is null)
            {
                return LocalizationYamlDocument.Invalid("localization.yaml 的 commonTextOverrides 必须是 mapping。");
            }

            overrides = ReadOverrides(overrideMap, out var overrideError);
            if (overrideError is not null)
            {
                return LocalizationYamlDocument.Invalid(overrideError);
            }
        }

        var urlPolicy = WorkspaceYaml.ReadScalar(root, "urlPolicy")?.Trim();
        return new LocalizationYamlDocument(false, null, null, primary, urlPolicy, enabled, custom, overrides);
    }

    /// <summary>写出完整本地化文件。空的 custom 和 overrides 也写出来，方便和默认文件对照。</summary>
    public static Task WriteAsync(
        string path,
        string primaryLanguage,
        string urlPolicy,
        IReadOnlyList<LanguageRecord> enabledLanguages,
        IReadOnlyList<LanguageRecord> customLanguages,
        IReadOnlyList<CommonI18nTextOverride> commonTextOverrides,
        CancellationToken cancellationToken)
    {
        var overrides = new YamlMappingNode();
        foreach (var item in commonTextOverrides.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var values = new YamlMappingNode();
            foreach (var pair in item.Values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(new YamlScalarNode(pair.Key), new YamlScalarNode(pair.Value));
            }

            overrides.Add(new YamlScalarNode(item.Key), values);
        }

        var root = new YamlMappingNode
        {
            { new YamlScalarNode("primaryLanguage"), new YamlScalarNode(primaryLanguage) },
            { new YamlScalarNode("urlPolicy"), new YamlScalarNode(urlPolicy) },
            { new YamlScalarNode("enabledLanguages"), WriteLanguages(enabledLanguages) },
            { new YamlScalarNode("customLanguages"), WriteLanguages(customLanguages) },
            { new YamlScalarNode("commonTextOverrides"), overrides },
        };
        return WorkspaceYaml.WriteAsync(path, root, Header, cancellationToken);
    }

    private static List<LanguageRecord> ReadLanguages(YamlSequenceNode sequence, string field, out string? error)
    {
        var list = new List<LanguageRecord>();
        foreach (var child in sequence.Children)
        {
            if (child is not YamlMappingNode map)
            {
                error = $"localization.yaml 的 {field} 项必须是 mapping。";
                return [];
            }

            var code = WorkspaceYaml.ReadScalar(map, "code")?.Trim();
            var nativeName = WorkspaceYaml.ReadScalar(map, "nativeName")?.Trim();
            var englishName = WorkspaceYaml.ReadScalar(map, "englishName")?.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(nativeName) || string.IsNullOrWhiteSpace(englishName))
            {
                error = $"localization.yaml 的 {field} 必须包含 code、nativeName、englishName。";
                return [];
            }

            list.Add(new LanguageRecord
            {
                Code = code,
                NativeName = nativeName,
                EnglishName = englishName,
            });
        }

        error = null;
        return list;
    }

    private static List<CommonI18nTextOverride> ReadOverrides(YamlMappingNode map, out string? error)
    {
        var list = new List<CommonI18nTextOverride>();
        foreach (var pair in map.Children)
        {
            if (pair.Key is not YamlScalarNode key || string.IsNullOrWhiteSpace(key.Value))
            {
                continue;
            }

            if (pair.Value is not YamlMappingNode values)
            {
                error = $"localization.yaml 的 commonTextOverrides.{key.Value} 必须是语言到文本的 mapping。";
                return [];
            }

            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values.Children)
            {
                if (value.Key is not YamlScalarNode lang || value.Value is not YamlScalarNode text)
                {
                    error = "localization.yaml 的 commonTextOverrides 只能是字符串。";
                    return [];
                }

                if (!string.IsNullOrWhiteSpace(lang.Value))
                {
                    texts[lang.Value.Trim()] = text.Value ?? string.Empty;
                }
            }

            list.Add(new CommonI18nTextOverride
            {
                Key = key.Value.Trim(),
                Values = texts,
            });
        }

        error = null;
        return list;
    }

    private static YamlSequenceNode WriteLanguages(IReadOnlyList<LanguageRecord> languages)
    {
        var sequence = new YamlSequenceNode();
        foreach (var language in languages)
        {
            sequence.Add(new YamlMappingNode
            {
                { new YamlScalarNode("code"), new YamlScalarNode(language.Code) },
                { new YamlScalarNode("nativeName"), new YamlScalarNode(language.NativeName) },
                { new YamlScalarNode("englishName"), new YamlScalarNode(language.EnglishName) },
            });
        }

        return sequence;
    }
}
