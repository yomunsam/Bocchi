using YamlDotNet.RepresentationModel;

namespace Bocchi.Workspace.Content;

/// <summary>
/// YAML 字段访问的薄包装。所有 Loader 都通过它读取 frontmatter 字段，统一类型转换与错误风格。
/// </summary>
internal static class YamlAccess
{
    public static YamlMappingNode? Parse(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        if (stream.Documents.Count == 0)
        {
            return null;
        }

        return stream.Documents[0].RootNode as YamlMappingNode;
    }

    public static string? GetString(YamlMappingNode mapping, string key)
    {
        if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar)
        {
            return scalar.Value;
        }

        return null;
    }

    public static bool? GetBool(YamlMappingNode mapping, string key)
    {
        var raw = GetString(mapping, key);
        if (raw is null)
        {
            return null;
        }

        return bool.TryParse(raw, out var b) ? b : null;
    }

    public static int? GetInt(YamlMappingNode mapping, string key)
    {
        var raw = GetString(mapping, key);
        if (raw is null)
        {
            return null;
        }

        return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    public static IReadOnlyList<string> GetStringList(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) || node is not YamlSequenceNode seq)
        {
            return [];
        }

        var list = new List<string>(seq.Children.Count);
        foreach (var child in seq.Children)
        {
            if (child is YamlScalarNode s && s.Value is { } v)
            {
                list.Add(v);
            }
        }

        return list;
    }

    public static YamlSequenceNode? GetSequence(YamlMappingNode mapping, string key)
    {
        if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlSequenceNode seq)
        {
            return seq;
        }

        return null;
    }

    public static YamlMappingNode? GetMapping(YamlMappingNode mapping, string key)
    {
        if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlMappingNode m)
        {
            return m;
        }

        return null;
    }
}