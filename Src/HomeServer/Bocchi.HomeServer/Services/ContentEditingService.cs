using System.Globalization;

using Bocchi.ContentModel;
using Bocchi.Workspace;
using Bocchi.Workspace.Content;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Admin Markdown 编辑服务。它只读写内容 workspace 文件，不把正文复制进 Home Server 数据库。
/// </summary>
public sealed class ContentEditingService
{
    /// <summary>Page slug 会占用站点顶层 URL，这些保留段由系统路由或固定内容类型使用。</summary>
    private static readonly HashSet<string> ReservedTopLevelPageSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "account",
        "admin",
        "api",
        "assets",
        "friends",
        "healthz",
        "media",
        "notes",
        "posts",
        "setup",
        "works",
        "_content",
        "_framework",
    };

    private readonly BocchiDataLayout _layout;
    private readonly MarkdownPipeline _markdown;
    private readonly TimeProvider _time;

    /// <summary>构造内容编辑服务。</summary>
    public ContentEditingService(BocchiDataLayout layout, MarkdownPipeline markdown, TimeProvider time)
    {
        _layout = layout;
        _markdown = markdown;
        _time = time;
    }

    /// <summary>读取一个内容 workspace 内的 Markdown/YAML 文件。</summary>
    public async Task<EditableContentFile> ReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveContentFile(relativePath);
        var raw = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var split = FrontmatterParser.Split(raw);
        return new EditableContentFile(
            relativePath,
            split.Yaml,
            split.Body,
            _markdown.RenderHtml(split.Body),
            File.GetLastWriteTimeUtc(fullPath));
    }

    /// <summary>创建指定类型的 Markdown 草稿，并返回可编辑内容。</summary>
    public Task<EditableContentFile> CreateDraftAsync(ContentKind kind, CancellationToken cancellationToken = default)
        => kind switch
        {
            ContentKind.Post => CreatePostDraftAsync(cancellationToken),
            ContentKind.Page => CreatePageDraftAsync(cancellationToken),
            ContentKind.Work => CreateWorkDraftAsync(cancellationToken),
            _ => throw new InvalidOperationException("这个内容类型暂时没有完整 Markdown 编辑器。"),
        };

    /// <summary>创建一个新的 Post 草稿文件，并返回可编辑内容。</summary>
    public async Task<EditableContentFile> CreatePostDraftAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var year = now.Year.ToString(CultureInfo.InvariantCulture);
        var slug = CreateUniqueYearScopedSlug(_layout.Workspace.PostsDirectory, year, "new-post");
        var directory = Path.Combine(_layout.Workspace.PostsDirectory, year, slug);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "index.md");
        var relativePath = _layout.Workspace.ToRelative(file);
        var yaml = $"""
            title: New Post
            slug: {slug}
            status: draft
            updatedAt: {FormatDateTime(now)}
            category:
            tags: []
            summary:
            """;
        await SaveNewAsync(relativePath, yaml, "# New Post\n\n", cancellationToken).ConfigureAwait(false);
        return await ReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>创建一个新的 Page 草稿文件，并返回可编辑内容。</summary>
    public async Task<EditableContentFile> CreatePageDraftAsync(CancellationToken cancellationToken = default)
    {
        var slug = CreateUniqueSlug(_layout.Workspace.PagesDirectory, "new-page");
        var directory = Path.Combine(_layout.Workspace.PagesDirectory, slug);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "index.md");
        var relativePath = _layout.Workspace.ToRelative(file);
        var yaml = $"""
            title: New Page
            slug: {slug}
            status: draft
            template: normal
            order: 0
            showInNavigation: false
            """;
        await SaveNewAsync(relativePath, yaml, string.Empty, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>创建一个新的 Work 草稿文件；作品仍然复用 Markdown 编辑器和年份目录约定。</summary>
    public async Task<EditableContentFile> CreateWorkDraftAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var year = now.Year.ToString(CultureInfo.InvariantCulture);
        var slug = CreateUniqueYearScopedSlug(_layout.Workspace.WorksDirectory, year, "new-work");
        var directory = Path.Combine(_layout.Workspace.WorksDirectory, year, slug);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "index.md");
        var relativePath = _layout.Workspace.ToRelative(file);
        var yaml = $"""
            title: New Work
            slug: {slug}
            status: draft
            role:
            period:
            stack: []
            summary:
            featured: false
            """;
        await SaveNewAsync(relativePath, yaml, "# New Work\n\n", cancellationToken).ConfigureAwait(false);
        return await ReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存 frontmatter 与 Markdown 正文，并保持文件仍是单一事实来源。</summary>
    public async Task SaveAsync(string relativePath, string yaml, string markdown, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveContentFile(relativePath);
        var normalizedYaml = (yaml ?? string.Empty).Trim();
        var normalizedBody = (markdown ?? string.Empty).Replace("\r\n", "\n");
        var content = string.IsNullOrWhiteSpace(normalizedYaml)
            ? normalizedBody
            : $"---\n{normalizedYaml}\n---\n{normalizedBody}";
        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>规范化候选路径标识，并按最终 URL 作用域检查是否已被其它内容占用。</summary>
    public ContentSlugValidationResult ValidateUrlSlug(ContentKind kind, string currentRelativePath, string? candidate)
    {
        var slug = ContentSlug.Normalize(candidate);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return ContentSlugValidationResult.Unavailable(slug, "路径标识不能为空。");
        }

        var currentFullPath = ResolveContentFile(currentRelativePath);
        return kind switch
        {
            ContentKind.Page => ValidatePageSlug(currentFullPath, slug),
            ContentKind.Post => ValidateYearScopedSlug(currentRelativePath, currentFullPath, _layout.Workspace.PostsDirectory, "posts", slug),
            _ => ContentSlugValidationResult.Available(slug),
        };
    }

    private async Task SaveNewAsync(string relativePath, string yaml, string markdown, CancellationToken cancellationToken)
    {
        var fullPath = ResolveContentPathForNewFile(relativePath);
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException("内容文件已经存在。");
        }

        var normalizedYaml = (yaml ?? string.Empty).Trim();
        var normalizedBody = (markdown ?? string.Empty).Replace("\r\n", "\n");
        var content = string.IsNullOrWhiteSpace(normalizedYaml)
            ? normalizedBody
            : $"---\n{normalizedYaml}\n---\n{normalizedBody}";
        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>删除一个内容 workspace 内的源文件，并清理它产生的空目录。</summary>
    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveContentFile(relativePath);
        File.Delete(fullPath);
        PruneEmptyDirectories(Path.GetDirectoryName(fullPath));
        return Task.CompletedTask;
    }

    /// <summary>把内容相对路径转成编辑页 URL。</summary>
    public static string EditUrl(string relativePath)
        => "/Admin/Content/Edit?path=" + Uri.EscapeDataString(relativePath.Replace('\\', '/'));

    private string ResolveContentFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, '/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("内容路径不能包含上级目录跳转。");
        }

        var root = Path.GetFullPath(_layout.WorkspaceRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("内容路径必须位于 workspace 内。");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到要编辑的内容文件。", fullPath);
        }

        return fullPath;
    }

    private string ResolveContentPathForNewFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, '/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("内容路径不能包含上级目录跳转。");
        }

        var root = Path.GetFullPath(_layout.WorkspaceRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("内容路径必须位于 workspace 内。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        return fullPath;
    }

    private static string CreateUniqueSlug(string root, string prefix)
    {
        var candidate = prefix;
        var index = 2;
        while (Directory.Exists(Path.Combine(root, candidate)))
        {
            candidate = $"{prefix}-{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            index++;
        }

        return candidate;
    }

    private static string CreateUniqueYearScopedSlug(string root, string year, string prefix)
        => CreateUniqueSlug(Path.Combine(root, year), prefix);

    private static string FormatDateTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private ContentSlugValidationResult ValidatePageSlug(string currentFullPath, string slug)
    {
        if (ReservedTopLevelPageSlugs.Contains(slug))
        {
            return ContentSlugValidationResult.Unavailable(slug, "这个路径标识会与系统路由冲突。");
        }

        if (IsSlugTaken(_layout.Workspace.PagesDirectory, currentFullPath, slug))
        {
            return ContentSlugValidationResult.Unavailable(slug, "这个路径标识已经被其它页面使用。");
        }

        return ContentSlugValidationResult.Available(slug);
    }

    private static ContentSlugValidationResult ValidateYearScopedSlug(
        string currentRelativePath,
        string currentFullPath,
        string contentRoot,
        string expectedKindDirectory,
        string slug)
    {
        if (!TryReadYearFromRelativePath(currentRelativePath, expectedKindDirectory, out var year))
        {
            return ContentSlugValidationResult.Unavailable(slug, "当前内容路径无法判断发布年份。");
        }

        var yearRoot = Path.Combine(contentRoot, year);
        return IsSlugTaken(yearRoot, currentFullPath, slug)
            ? ContentSlugValidationResult.Unavailable(slug, "这个路径标识已经被同一年份的其它文章使用。")
            : ContentSlugValidationResult.Available(slug);
    }

    private static bool IsSlugTaken(string root, string currentFullPath, string slug)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }

        var current = Path.GetFullPath(currentFullPath);
        foreach (var indexFile in Directory.EnumerateFiles(root, "index.md", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFullPath(indexFile), current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existing = ReadSlugFromMarkdownFile(indexFile);
            if (string.Equals(existing, slug, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadSlugFromMarkdownFile(string indexFile)
    {
        var fallback = Path.GetFileName(Path.GetDirectoryName(indexFile)) ?? string.Empty;
        try
        {
            var raw = File.ReadAllText(indexFile);
            var split = FrontmatterParser.Split(raw);
            var mapping = ParseYaml(split.Yaml);
            var slug = ReadYamlString(mapping, "slug");
            return ContentSlug.Normalize(string.IsNullOrWhiteSpace(slug) ? fallback : slug);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlException)
        {
            return ContentSlug.Normalize(fallback);
        }
    }

    private static YamlMappingNode? ParseYaml(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);
        return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
    }

    private static string? ReadYamlString(YamlMappingNode? root, string key)
    {
        if (root?.Children.TryGetValue(new YamlScalarNode(key), out var node) == true &&
            node is YamlScalarNode scalar &&
            !string.IsNullOrWhiteSpace(scalar.Value))
        {
            return scalar.Value.Trim();
        }

        return null;
    }

    private static bool TryReadYearFromRelativePath(string relativePath, string expectedKindDirectory, out string year)
    {
        year = string.Empty;
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || !string.Equals(parts[0], expectedKindDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        year = parts[1];
        return year.Length == 4 && year.All(char.IsDigit);
    }

    private void PruneEmptyDirectories(string? start)
    {
        var root = Path.GetFullPath(_layout.WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
        var current = start;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var full = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
                || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || Directory.EnumerateFileSystemEntries(full).Any())
            {
                return;
            }

            Directory.Delete(full);
            current = Path.GetDirectoryName(full);
        }
    }
}

/// <summary>编辑器页面读取到的文件内容。</summary>
/// <param name="RelativePath">内容 workspace 相对路径。</param>
/// <param name="Yaml">frontmatter YAML，不含分隔符。</param>
/// <param name="Markdown">Markdown 正文。</param>
/// <param name="PreviewHtml">使用统一 Markdig pipeline 渲染的预览 HTML。</param>
/// <param name="LastModifiedUtc">源文件最后修改时间。</param>
public sealed record EditableContentFile(
    string RelativePath,
    string Yaml,
    string Markdown,
    string PreviewHtml,
    DateTime LastModifiedUtc);
