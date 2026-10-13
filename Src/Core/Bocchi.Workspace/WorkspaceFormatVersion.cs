using System.Globalization;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.Workspace;

/// <summary>
/// workspace 格式版本。写在 <c>site/site.yaml</c> 顶层 <c>formatVersion</c>。
/// 缺省（老目录无字段）视为 <see cref="DefaultWhenMissing"/>；读到更高版本时拒绝写入与会改状态的构建。
/// </summary>
public static class WorkspaceFormatVersion
{
    /// <summary>当前程序认识的 workspace 格式版本。</summary>
    public const int Current = 1;

    /// <summary>缺省（无字段）时按此版本解释，便于 rc 前无实例过渡。</summary>
    public const int DefaultWhenMissing = 1;

    /// <summary>
    /// 从 <c>site.yaml</c> 文件读取 <c>formatVersion</c>。
    /// 文件不存在或为空 → <see cref="DefaultWhenMissing"/> 且返回 true；
    /// 有字段则解析；YAML 无法解析为 mapping 时返回 false。
    /// </summary>
    public static bool TryReadFromFile(string siteYamlPath, out int formatVersion)
    {
        formatVersion = DefaultWhenMissing;
        if (string.IsNullOrWhiteSpace(siteYamlPath)
            || !File.Exists(siteYamlPath)
            || new FileInfo(siteYamlPath).Length == 0)
        {
            return true;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(siteYamlPath);
        }
        catch (IOException)
        {
            return false;
        }

        return TryReadFromYamlText(raw, out formatVersion);
    }

    /// <summary>
    /// 从 YAML 文本读取 <c>formatVersion</c>。空文本 → 缺省版本且返回 true；
    /// 非 mapping 或解析失败 → 返回 false。
    /// </summary>
    public static bool TryReadFromYamlText(string? yaml, out int formatVersion)
    {
        formatVersion = DefaultWhenMissing;
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return true;
        }

        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0)
            {
                return true;
            }

            if (stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return false;
            }

            formatVersion = ReadFormatVersion(root);
            return true;
        }
        catch (YamlException)
        {
            return false;
        }
    }

    /// <summary>从已解析的 mapping 读取；缺字段时返回 <see cref="DefaultWhenMissing"/>。</summary>
    public static int ReadFormatVersion(YamlMappingNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var key = new YamlScalarNode("formatVersion");
        if (!root.Children.TryGetValue(key, out var node) || node is not YamlScalarNode scalar
            || string.IsNullOrWhiteSpace(scalar.Value))
        {
            return DefaultWhenMissing;
        }

        return int.TryParse(scalar.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : DefaultWhenMissing;
    }

    /// <summary>
    /// 若 <paramref name="formatVersion"/> 比 <see cref="Current"/> 更新则抛出，
    /// 阻止 Save / 启动 Import 等会改状态的写入。
    /// </summary>
    public static void ThrowIfNewerThanCurrent(int formatVersion)
    {
        if (formatVersion > Current)
        {
            throw new InvalidOperationException(
                $"site.yaml formatVersion={formatVersion} 比当前 Bocchi 支持的 {Current} 更新。请升级 Bocchi，或恢复较旧的 workspace。拒绝写入与灌库。");
        }
    }

    /// <summary>
    /// 读取已有 <c>site.yaml</c> 并在过新时抛出。文件缺失或为空视为可写（将写入 Current）。
    /// 文件存在但无法解析时也拒绝，以免误把新格式改写成 Current。
    /// </summary>
    public static void EnsureExistingFileAllowsWrite(string siteYamlPath)
    {
        if (string.IsNullOrWhiteSpace(siteYamlPath)
            || !File.Exists(siteYamlPath)
            || new FileInfo(siteYamlPath).Length == 0)
        {
            return;
        }

        if (!TryReadFromFile(siteYamlPath, out var formatVersion))
        {
            throw new InvalidOperationException(
                "无法解析 site.yaml 的 formatVersion，拒绝写入以免破坏 workspace。");
        }

        ThrowIfNewerThanCurrent(formatVersion);
    }
}
