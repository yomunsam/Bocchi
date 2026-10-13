using System.Globalization;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.Workspace;

/// <summary>
/// workspace 格式版本。写在 <c>site/site.yaml</c> 顶层 <c>formatVersion</c>。
/// 缺省（老目录无字段）视为 <see cref="DefaultWhenMissing"/>；读到更高版本时拒绝写入与会改状态的构建。
/// 分类树和前台语言文件不再单独编号，世代跟这份 <c>formatVersion</c>。
/// </summary>
public static class WorkspaceFormatVersion
{
    /// <summary>当前程序认识的 workspace 格式版本。</summary>
    public const int Current = 1;

    /// <summary>缺省（无字段）时按此版本解释，便于 rc 前无实例过渡。</summary>
    public const int DefaultWhenMissing = 1;

    /// <summary>文件比当前程序新。构建和会改状态的写入都应拒绝。</summary>
    public const string UnsupportedCode = "SITE_FORMAT_VERSION_UNSUPPORTED";

    /// <summary>字段存在但小于 1。与「缺省当 1」不同，显式非法值要拒绝。</summary>
    public const string InvalidCode = "SITE_FORMAT_VERSION_INVALID";

    /// <summary>
    /// 解释已经取出的 <c>formatVersion</c>。 <paramref name="raw"/> 为 null 表示字段缺失，按 <see cref="DefaultWhenMissing"/>。
    /// 不受支持时返回 false，并带上 <paramref name="errorCode"/> 与 <paramref name="message"/>。
    /// </summary>
    public static bool TryInterpret(int? raw, out int version, out string? errorCode, out string? message)
    {
        version = raw ?? DefaultWhenMissing;
        if (version > Current)
        {
            errorCode = UnsupportedCode;
            message = $"site.yaml formatVersion={version} 比当前 Bocchi 支持的 {Current} 更新。请升级 Bocchi，或恢复较旧的 workspace。";
            return false;
        }

        if (version < 1)
        {
            errorCode = InvalidCode;
            message = $"site.yaml formatVersion={version} 无效。";
            return false;
        }

        errorCode = null;
        message = null;
        return true;
    }

    /// <summary>
    /// 从 <c>site.yaml</c> 全文读取 <c>formatVersion</c>。
    /// 空白文本视为缺省，返回 true。
    /// YAML 无法解析，或根节点不是 mapping 时返回 false，且 <paramref name="errorCode"/> 为 null（这不是版本拒绝）。
    /// 版本过新或小于 1 时返回 false，并带上错误码。
    /// </summary>
    public static bool TryRead(string? yaml, out int version, out string? errorCode, out string? message)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return TryInterpret(null, out version, out errorCode, out message);
        }

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                version = 0;
                errorCode = null;
                message = "site.yaml 内容为空或根节点不是 mapping。";
                return false;
            }

            int? raw = null;
            if (mapping.Children.TryGetValue(new YamlScalarNode("formatVersion"), out var node)
                && node is YamlScalarNode scalar
                && int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                raw = parsed;
            }

            return TryInterpret(raw, out version, out errorCode, out message);
        }
        catch (YamlException ex)
        {
            version = 0;
            errorCode = null;
            message = $"site.yaml 解析失败：{ex.Message}";
            return false;
        }
    }
}
