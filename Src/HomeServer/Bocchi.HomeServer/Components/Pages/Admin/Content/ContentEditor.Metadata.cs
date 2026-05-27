using System.Globalization;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace.Scanning;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>ContentEditor 的内容类型判断与 frontmatter YAML 同步逻辑。</summary>
public partial class ContentEditor
{
    /// <summary>把树形分类压平成下拉选项，并保留层级深度用于展示缩进。</summary>
    private static List<CategoryOption> FlattenCategoryOptions(IEnumerable<CategoryTreeNode> roots)
    {
        var options = new List<CategoryOption>();
        foreach (var root in roots)
        {
            AppendCategoryOption(root, depth: 0, options);
        }

        return options;
    }

    /// <summary>递归展开分类树；保存值始终使用节点 Name，而不是 URL slug。</summary>
    private static void AppendCategoryOption(CategoryTreeNode node, int depth, List<CategoryOption> options)
    {
        options.Add(new CategoryOption(node.Name, depth));
        foreach (var child in node.Children)
        {
            AppendCategoryOption(child, depth + 1, options);
        }
    }

    /// <summary>生成分类下拉框的人类可读标签，子级用破折号表示层级。</summary>
    private static string CategoryOptionLabel(CategoryOption option)
        => option.Depth == 0
            ? option.Name
            : $"{string.Concat(Enumerable.Repeat("— ", option.Depth))}{option.Name}";

    /// <summary>从 YAML 载入常用字段；未知字段保留在原始 YAML 中，保存时不会被主动删除。</summary>
    private void LoadMetadataFromYaml(EditableContentFile file)
        => LoadMetadataFromYaml(file.Yaml, SlugFromPath(file.RelativePath));

    /// <summary>从 YAML 载入常用字段；临时草稿会使用内容类型默认 slug 作为 fallback。</summary>
    private void LoadMetadataFromYaml(string yaml, string fallbackSlug)
    {
        var root = ParseYaml(yaml);
        _title = ReadString(root, "title") ?? string.Empty;
        _slug = ReadString(root, "slug") ?? fallbackSlug;
        _status = NormalizeStatus(ReadString(root, "status")).ToString();
        _pathLocked = ReadBool(root, "pathLocked") ?? (CurrentStatus == ContentStatus.Published);
        _pathLockedAtLoad = _pathLocked;
        _slugTouchedInSession = false;
        _summary = ReadString(root, "summary") ?? string.Empty;
        _category = ReadString(root, "category") ?? string.Empty;
        _tagsText = string.Join(", ", ReadStringList(root, "tags"));
        _publishedAt = ReadString(root, "publishedAt") ?? string.Empty;
        _template = ReadString(root, "template") ?? "normal";
        _showInNavigation = ReadBool(root, "showInNavigation") ?? false;
        _order = ReadInt(root, "order") ?? 0;
    }

    /// <summary>把结构化字段写回 YAML，同时保留用户在 Frontmatter 中添加的其他键。</summary>
    private bool TryBuildYamlFromFields(out string yaml, out string? error)
        => TryBuildYamlFromFields(refreshUpdatedAt: true, out yaml, out error);

    /// <summary>把结构化字段写回 YAML；差异快照不能刷新 updatedAt，只有真实保存才更新时间戳。</summary>
    private bool TryBuildYamlFromFields(bool refreshUpdatedAt, out string yaml, out string? error)
    {
        yaml = string.Empty;
        error = null;

        YamlMappingNode root;
        if (string.IsNullOrWhiteSpace(_yaml))
        {
            root = new YamlMappingNode();
        }
        else
        {
            try
            {
                root = ParseYamlStrict(_yaml) ?? new YamlMappingNode();
            }
            catch (YamlException ex)
            {
                error = FormatText("contentEditor.yaml.parseErrorFormat", ex.Message);
                return false;
            }
        }

        SetScalar(root, "title", _title);
        SetScalar(root, "slug", SlugForYaml());
        SetScalar(root, "status", StatusYamlValue(CurrentStatus));
        SetScalar(root, "summary", _summary, removeWhenBlank: true);
        SetScalar(root, "pathLocked", _pathLocked ? "true" : null, removeWhenBlank: true);

        if (IsPostFile)
        {
            SetScalar(root, "category", _category, removeWhenBlank: true);
            SetSequence(root, "tags", ParseCommaList(_tagsText));
            SetScalar(root, "publishedAt", _publishedAt, removeWhenBlank: true);
            if (refreshUpdatedAt)
            {
                SetScalar(root, "updatedAt", FormatDateTime(Time.GetUtcNow()));
            }
        }

        if (IsPageFile)
        {
            SetScalar(root, "template", string.IsNullOrWhiteSpace(_template) ? "normal" : _template);
            SetScalar(root, "order", _order.ToString(CultureInfo.InvariantCulture));
            SetScalar(root, "showInNavigation", _showInNavigation ? "true" : "false");
        }

        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        yaml = writer.ToString().Trim();
        return true;
    }

    /// <summary>跨平台拆分文本行，仅用于保存前差异摘要。</summary>
    private static string[] SplitLines(string value)
        => value.Replace("\r\n", "\n").Split('\n');

    /// <summary>根据临时草稿、文件路径或查询参数判断当前内容类型。</summary>
    private ContentKind? CurrentKind()
    {
        if (_draftSession is not null)
        {
            return _draftSession.Kind;
        }

        var relativePath = _file?.RelativePath ?? Path;
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("pages/", StringComparison.OrdinalIgnoreCase))
        {
            return ContentKind.Page;
        }

        if (normalized.StartsWith("works/", StringComparison.OrdinalIgnoreCase))
        {
            return ContentKind.Work;
        }

        return normalized.StartsWith("posts/", StringComparison.OrdinalIgnoreCase) ? ContentKind.Post : null;
    }

    /// <summary>解析创建入口传入的内容类型，只接受当前完整编辑器支持的类型。</summary>
    private static bool TryParseCreateKind(string? value, out ContentKind kind)
    {
        kind = default;
        return !string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, ignoreCase: true, out kind) &&
            kind is ContentKind.Post or ContentKind.Page or ContentKind.Work;
    }

    /// <summary>判断路径是否指向 Markdown 文件。</summary>
    private static bool IsMarkdownPath(string? path)
        => path?.EndsWith(".md", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>判断路径是否是 <c>index.{language}.md</c> 形态的非默认语言版本文件。</summary>
    private static bool IsVariantIndexMarkdownPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileName = System.IO.Path.GetFileName(path.Replace('\\', '/'));
        return fileName.StartsWith("index.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从目录型内容路径推导 slug fallback。</summary>
    private static string SlugFromPath(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && (string.Equals(parts[0], "posts", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(parts[0], "works", StringComparison.OrdinalIgnoreCase)))
        {
            return parts[2];
        }

        return parts.Length >= 2 && string.Equals(parts[0], "pages", StringComparison.OrdinalIgnoreCase)
            ? parts[1]
            : "draft";
    }

    /// <summary>把 frontmatter 中的 status 字符串收束为内容状态枚举。</summary>
    private static ContentStatus NormalizeStatus(string? value)
        => Enum.TryParse<ContentStatus>(value, ignoreCase: true, out var status)
            ? status
            : ContentStatus.Draft;

    /// <summary>把内容状态写回 YAML 中使用的小写值。</summary>
    private static string StatusYamlValue(ContentStatus status)
        => status switch
        {
            ContentStatus.Published => "published",
            ContentStatus.Archived => "archived",
            _ => "draft",
        };

    /// <summary>宽容解析 YAML；解析失败时返回空 mapping，让页面保留原始 YAML。</summary>
    private static YamlMappingNode? ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        try
        {
            return ParseYamlStrict(yaml);
        }
        catch (YamlException)
        {
            return null;
        }
    }

    /// <summary>严格解析 YAML frontmatter，调用方负责处理异常。</summary>
    private static YamlMappingNode? ParseYamlStrict(string yaml)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
    }

    /// <summary>从 YAML mapping 中读取非空 scalar 字符串。</summary>
    private static string? ReadString(YamlMappingNode? root, string key)
    {
        if (root?.Children.TryGetValue(new YamlScalarNode(key), out var node) == true &&
            node is YamlScalarNode scalar &&
            !string.IsNullOrWhiteSpace(scalar.Value))
        {
            return scalar.Value.Trim();
        }

        return null;
    }

    /// <summary>从 YAML mapping 中读取 bool 值。</summary>
    private static bool? ReadBool(YamlMappingNode? root, string key)
    {
        var value = ReadString(root, key);
        return bool.TryParse(value, out var result) ? result : null;
    }

    /// <summary>从 YAML mapping 中读取整数值。</summary>
    private static int? ReadInt(YamlMappingNode? root, string key)
    {
        var value = ReadString(root, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    /// <summary>从 YAML mapping 中读取字符串数组；兼容历史逗号分隔 scalar。</summary>
    private static List<string> ReadStringList(YamlMappingNode? root, string key)
    {
        if (root?.Children.TryGetValue(new YamlScalarNode(key), out var node) != true)
        {
            return [];
        }

        if (node is YamlSequenceNode sequence)
        {
            return sequence.Children
                .OfType<YamlScalarNode>()
                .Select(x => x.Value?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
        }

        return node is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value)
            ? ParseCommaList(scalar.Value)
            : [];
    }

    /// <summary>设置或删除 YAML scalar 字段。</summary>
    private static void SetScalar(YamlMappingNode root, string key, string? value, bool removeWhenBlank = false)
    {
        var scalarKey = new YamlScalarNode(key);
        if (removeWhenBlank && string.IsNullOrWhiteSpace(value))
        {
            root.Children.Remove(scalarKey);
            return;
        }

        root.Children[scalarKey] = new YamlScalarNode(value?.Trim() ?? string.Empty);
    }

    /// <summary>把字符串列表写入 YAML sequence 字段。</summary>
    private static void SetSequence(YamlMappingNode root, string key, IReadOnlyList<string> values)
    {
        var sequence = new YamlSequenceNode();
        foreach (var value in values)
        {
            sequence.Add(new YamlScalarNode(value));
        }

        root.Children[new YamlScalarNode(key)] = sequence;
    }

    /// <summary>解析 UI 中逗号分隔的标签文本。</summary>
    private static List<string> ParseCommaList(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

    /// <summary>按 frontmatter 约定格式化带时区的时间值。</summary>
    private static string FormatDateTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Post 分类下拉选项，Name 同时作为 UI 显示名与 frontmatter 保存值。</summary>
    private sealed record CategoryOption(string Name, int Depth);
}
