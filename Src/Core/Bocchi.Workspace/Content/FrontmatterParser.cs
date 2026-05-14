namespace Bocchi.Workspace.Content;

/// <summary>
/// 一份 frontmatter 解析结果：原始 YAML 文本 + 正文（去除 frontmatter 后的剩余 Markdown）。
/// </summary>
/// <param name="Yaml">frontmatter YAML 文本（不含 <c>---</c> 分隔符）。空字符串表示无 frontmatter。</param>
/// <param name="Body">去除 frontmatter 后的 Markdown 正文。</param>
public sealed record FrontmatterSplit(string Yaml, string Body);

/// <summary>
/// frontmatter 拆分器。仅负责把"原始文本"切分为"YAML 块"+"Markdown 正文"两段；
/// 把 YAML 反序列化为对象的工作由各 Loader 完成。
/// </summary>
public static class FrontmatterParser
{
    /// <summary>
    /// 从输入文本中拆出 frontmatter（首部 <c>---</c> ... <c>---</c> 块）与剩余正文。
    /// </summary>
    /// <remarks>
    /// 规则：
    /// <list type="bullet">
    ///   <item><description>frontmatter 必须在文件最开头（允许 BOM 与首部空白行被忽略）。</description></item>
    ///   <item><description>分隔符行为单独的、仅含 <c>---</c>（或 <c>--- </c>）的整行。</description></item>
    ///   <item><description>没有 frontmatter 时返回空 YAML + 全文作为 Body。</description></item>
    /// </list>
    /// </remarks>
    public static FrontmatterSplit Split(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // 去除 BOM
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        // 必须以 '---' 起始（允许首部空行）
        var index = 0;
        while (index < content.Length && (content[index] == '\r' || content[index] == '\n'))
        {
            index++;
        }

        // 找到第一行
        var firstLineEnd = content.IndexOf('\n', index);
        if (firstLineEnd < 0)
        {
            return new FrontmatterSplit(string.Empty, content);
        }

        var firstLine = content[index..firstLineEnd].TrimEnd('\r').TrimEnd();
        if (firstLine != "---")
        {
            return new FrontmatterSplit(string.Empty, content);
        }

        // 从下一行开始查找闭合 '---'
        var yamlStart = firstLineEnd + 1;
        var cursor = yamlStart;
        while (cursor < content.Length)
        {
            var lineEnd = content.IndexOf('\n', cursor);
            var lineSliceEnd = lineEnd < 0 ? content.Length : lineEnd;
            var line = content[cursor..lineSliceEnd].TrimEnd('\r').TrimEnd();
            if (line == "---")
            {
                var yaml = content[yamlStart..cursor].TrimEnd('\r', '\n');
                var bodyStart = lineEnd < 0 ? content.Length : lineEnd + 1;
                var body = bodyStart >= content.Length ? string.Empty : content[bodyStart..];
                // 正文首部去掉 1 个换行（如果存在），保持自然外观
                return new FrontmatterSplit(yaml, body);
            }

            if (lineEnd < 0)
            {
                break;
            }

            cursor = lineEnd + 1;
        }

        // 未找到闭合分隔符 → 整体视为正文，frontmatter 为空（让上层报告 Error）
        return new FrontmatterSplit(string.Empty, content);
    }
}