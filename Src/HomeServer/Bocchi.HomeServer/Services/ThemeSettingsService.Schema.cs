using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Bocchi.GeneratorContract;

namespace Bocchi.HomeServer.Services;

/// <summary>config-schema 读取、字段归一化和提交值写回。</summary>
public sealed partial class ThemeSettingsService
{
    private static string NormalizeConfigurationJson(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(configurationJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Theme 配置必须是 JSON object。");
        }

        return document.RootElement.GetRawText();
    }

    private static JsonObject ParseConfigurationObject(string configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(configurationJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static async Task<List<ThemeConfigGroupView>> LoadConfigGroupsAsync(
        string themeRoot,
        JsonObject configuration,
        CancellationToken cancellationToken)
    {
        var schemaPath = Path.Combine(themeRoot, "config-schema.json");
        if (!File.Exists(schemaPath))
        {
            return [];
        }

        await using var stream = new FileStream(
            schemaPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        JsonNode? node;
        try
        {
            node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return [];
        }

        if (node?["groups"] is not JsonArray groups)
        {
            return [];
        }

        var result = new List<ThemeConfigGroupView>();
        foreach (var groupNode in groups.OfType<JsonObject>())
        {
            var id = ReadString(groupNode["id"]);
            var title = ReadString(groupNode["title"]);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var fields = groupNode["fields"] is JsonArray fieldArray
                ? fieldArray.OfType<JsonObject>()
                    .Select(field => MapConfigField(field, configuration))
                    .Where(field => field is not null)
                    .Cast<ThemeConfigFieldView>()
                    .ToList()
                : [];
            result.Add(new ThemeConfigGroupView
            {
                Id = id.Trim(),
                Title = title.Trim(),
                Fields = fields,
            });
        }

        return result;
    }

    private static ThemeConfigFieldView? MapConfigField(JsonObject field, JsonObject configuration)
    {
        var key = ReadString(field["key"]);
        var title = ReadString(field["title"]);
        var typeName = ReadString(field["type"]);
        if (string.IsNullOrWhiteSpace(key) ||
            string.IsNullOrWhiteSpace(title) ||
            !TryMapFieldType(typeName, out var type))
        {
            return null;
        }

        var defaultValue = field["default"];
        var savedValue = TryGetNestedValue(configuration, key);
        var currentValue = savedValue ?? defaultValue;
        return new ThemeConfigFieldView
        {
            Key = key.Trim(),
            Type = type,
            Title = title.Trim(),
            Description = TrimOrNull(ReadString(field["description"])),
            TextFormat = NormalizeTextFormat(type, ReadString(field["textFormat"])),
            Placeholder = TrimOrNull(ReadString(field["placeholder"])),
            HelpText = TrimOrNull(ReadString(field["helpText"])),
            Required = ReadBool(field["required"]),
            Options = ReadStringOptions(field["options"]),
            TextValue = JsonNodeToText(currentValue),
            BooleanValue = JsonNodeToBool(currentValue),
            SelectedValues = JsonNodeToStringList(currentValue),
            LocalizedTextValues = JsonNodeToLocalizedText(savedValue),
            DefaultLocalizedTextValues = JsonNodeToLocalizedText(defaultValue),
            LocalizedTextListValues = JsonNodeToLocalizedTextList(savedValue),
            DefaultLocalizedTextListValues = JsonNodeToLocalizedTextList(defaultValue),
            DefaultText = TrimOrNull(JsonNodeToText(defaultValue)),
        };
    }

    private static void ApplySubmittedFieldValue(
        JsonObject configuration,
        ThemeConfigFieldView field,
        ThemeConfigValueInput input)
    {
        switch (field.Type)
        {
            case ThemeConfigFieldType.Boolean:
                SetNestedValue(configuration, field.Key, JsonValue.Create(ParseBoolean(input.Value)));
                break;
            case ThemeConfigFieldType.Number:
                ApplyNumberValue(configuration, field.Key, input.Value);
                break;
            case ThemeConfigFieldType.MultiSelect:
                ApplyMultiSelectValue(configuration, field, input.Values);
                break;
            case ThemeConfigFieldType.LocalizedText:
                ApplyLocalizedTextValue(configuration, field.Key, input.LocalizedValues);
                break;
            case ThemeConfigFieldType.LocalizedTextList:
                ApplyLocalizedTextListValue(configuration, field.Key, input.LocalizedListValues);
                break;
            case ThemeConfigFieldType.Select:
                ApplyStringValue(configuration, field, input.Value, validateOptions: true);
                break;
            case ThemeConfigFieldType.Group:
                break;
            default:
                ApplyStringValue(configuration, field, input.Value, validateOptions: false);
                break;
        }
    }

    private static void ApplyNumberValue(JsonObject configuration, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RemoveNestedValue(configuration, key);
            return;
        }

        if (!decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidOperationException($"Theme 配置字段 '{key}' 需要数字。");
        }

        SetNestedValue(configuration, key, JsonValue.Create(number));
    }

    private static void ApplyStringValue(
        JsonObject configuration,
        ThemeConfigFieldView field,
        string? value,
        bool validateOptions)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RemoveNestedValue(configuration, field.Key);
            return;
        }

        var normalized = value.Trim();
        if (validateOptions &&
            field.Options.Count > 0 &&
            !field.Options.Contains(normalized, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Theme 配置字段 '{field.Key}' 的选项无效。");
        }

        SetNestedValue(configuration, field.Key, JsonValue.Create(normalized));
    }

    private static void ApplyMultiSelectValue(
        JsonObject configuration,
        ThemeConfigFieldView field,
        IEnumerable<string> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => field.Options.Count == 0 || field.Options.Contains(value, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count == 0)
        {
            RemoveNestedValue(configuration, field.Key);
            return;
        }

        var array = new JsonArray();
        foreach (var value in normalized)
        {
            array.Add(value);
        }

        SetNestedValue(configuration, field.Key, array);
    }

    private static void ApplyLocalizedTextValue(
        JsonObject configuration,
        string key,
        IReadOnlyDictionary<string, string> values)
    {
        var normalized = values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => new KeyValuePair<string, string>(pair.Key.Trim(), pair.Value.Trim()))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0)
        {
            RemoveNestedValue(configuration, key);
            return;
        }

        var obj = new JsonObject();
        foreach (var (language, value) in normalized.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            obj[language] = value;
        }

        SetNestedValue(configuration, key, obj);
    }

    private static void ApplyLocalizedTextListValue(
        JsonObject configuration,
        string key,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        var obj = new JsonObject();
        foreach (var (language, rawValues) in values.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)))
        {
            var normalized = rawValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (normalized.Count == 0)
            {
                continue;
            }

            var array = new JsonArray();
            foreach (var value in normalized)
            {
                array.Add(value);
            }

            obj[language.Trim()] = array;
        }

        if (obj.Count == 0)
        {
            RemoveNestedValue(configuration, key);
            return;
        }

        SetNestedValue(configuration, key, obj);
    }

    private static JsonNode? TryGetNestedValue(JsonObject root, string dottedKey)
    {
        var segments = SplitDottedKey(dottedKey);
        if (segments.Length == 0)
        {
            return null;
        }

        JsonNode? current = root;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static void SetNestedValue(JsonObject root, string dottedKey, JsonNode? value)
    {
        var segments = SplitDottedKey(dottedKey);
        if (segments.Length == 0)
        {
            return;
        }

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }

    private static void RemoveNestedValue(JsonObject root, string dottedKey)
    {
        var segments = SplitDottedKey(dottedKey);
        if (segments.Length == 0)
        {
            return;
        }

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                return;
            }

            current = child;
        }

        current.Remove(segments[^1]);
    }

    private static string[] SplitDottedKey(string dottedKey)
        => dottedKey.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryMapFieldType(string? value, out ThemeConfigFieldType type)
    {
        type = ThemeConfigFieldType.String;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() switch
        {
            "string" => SetType(ThemeConfigFieldType.String, out type),
            "number" => SetType(ThemeConfigFieldType.Number, out type),
            "boolean" => SetType(ThemeConfigFieldType.Boolean, out type),
            "select" => SetType(ThemeConfigFieldType.Select, out type),
            "multiSelect" => SetType(ThemeConfigFieldType.MultiSelect, out type),
            "color" => SetType(ThemeConfigFieldType.Color, out type),
            "image" => SetType(ThemeConfigFieldType.Image, out type),
            "url" => SetType(ThemeConfigFieldType.Url, out type),
            "localizedText" => SetType(ThemeConfigFieldType.LocalizedText, out type),
            "localizedTextList" => SetType(ThemeConfigFieldType.LocalizedTextList, out type),
            "group" => SetType(ThemeConfigFieldType.Group, out type),
            _ => Enum.TryParse(value, ignoreCase: true, out type),
        };
    }

    private static bool SetType(ThemeConfigFieldType value, out ThemeConfigFieldType type)
    {
        type = value;
        return true;
    }

    /// <summary>归一化文本字段的表现层格式；非文本字段和未知格式都按 plain 处理。</summary>
    private static string NormalizeTextFormat(ThemeConfigFieldType type, string? value)
    {
        if (type is not (ThemeConfigFieldType.String or ThemeConfigFieldType.LocalizedText))
        {
            return "plain";
        }

        return string.Equals(value?.Trim(), "inlineColor", StringComparison.OrdinalIgnoreCase)
            ? "inlineColor"
            : "plain";
    }

    private static string? ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;

    private static bool ReadBool(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static bool ParseBoolean(string? value)
        => bool.TryParse(value, out var result) && result;

    private static List<string> ReadStringOptions(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(ReadString)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string JsonNodeToText(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : node.ToJsonString(JsonOptions);
    }

    private static bool JsonNodeToBool(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return false;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out boolean) && boolean;
    }

    private static List<string> JsonNodeToStringList(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            return array
                .Select(JsonNodeToText)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        var single = JsonNodeToText(node);
        return string.IsNullOrWhiteSpace(single) ? [] : [single];
    }

    private static Dictionary<string, string> JsonNodeToLocalizedText(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return [];
        }

        return obj
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new KeyValuePair<string, string>(pair.Key.Trim(), JsonNodeToText(pair.Value).Trim()))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<string>> JsonNodeToLocalizedTextList(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return [];
        }

        return obj
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .Select(pair => new KeyValuePair<string, IReadOnlyList<string>>(pair.Key.Trim(), JsonNodeToStringList(pair.Value)))
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
