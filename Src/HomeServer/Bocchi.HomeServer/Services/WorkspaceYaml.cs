using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// <c>site/</c> 下 YAML 文件的读写。风格与 <c>navigation.yaml</c> 相同：camelCase 键，<c>YamlStream.Save</c> 的缩进。
/// </summary>
internal static class WorkspaceYaml
{
    /// <summary>
    /// 读出根 mapping。文件不存在或空白时 <paramref name="root"/> 和 <paramref name="error"/> 都是 null。
    /// 解析失败或根不是 mapping 时 <paramref name="error"/> 有说明。
    /// </summary>
    public static async Task<(YamlMappingNode? Root, string? Error)> LoadMappingAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return (null, null);
        }

        var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(raw);
            stream.Load(reader);
            if (stream.Documents.Count == 0)
            {
                return (null, null);
            }

            if (stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return (null, "根节点不是 mapping。");
            }

            return (root, null);
        }
        catch (YamlException ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>写出 mapping。可选的首行注释用普通文本写在文档前面，YamlDotNet 不会保留文件里原有注释。</summary>
    public static async Task WriteAsync(string path, YamlMappingNode root, string? headerComment, CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var stream = new YamlStream(new YamlDocument(root));
        await using var output = File.Create(path);
        await using var writer = new StreamWriter(output);
        if (!string.IsNullOrWhiteSpace(headerComment))
        {
            await writer.WriteLineAsync(headerComment.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        stream.Save(writer, assignAnchors: false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取标量；键不存在或不是标量时返回 null。</summary>
    public static string? ReadScalar(YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value
            : null;

    /// <summary>读取子 mapping。</summary>
    public static YamlMappingNode? ReadMapping(YamlMappingNode map, string key)
        => map.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlMappingNode child ? child : null;

    /// <summary>读取子 sequence。键不存在时返回 null；键存在但类型不对时 <paramref name="typeError"/> 为 true。</summary>
    public static YamlSequenceNode? ReadSequence(YamlMappingNode map, string key, out bool typeError)
    {
        typeError = false;
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return null;
        }

        if (node is YamlSequenceNode seq)
        {
            return seq;
        }

        typeError = true;
        return null;
    }
}
