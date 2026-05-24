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

    /// <summary>保存 frontmatter 与 Markdown 正文；允许改路径时，slug 管理的目录型内容会随 slug 移动源目录。</summary>
    public async Task<EditableContentFile> SaveAsync(
        string relativePath,
        string yaml,
        string markdown,
        bool allowPathRename = true,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveContentFile(relativePath);
        var normalizedRelativePath = NormalizeContentRelativePath(relativePath);
        var normalizedYaml = (yaml ?? string.Empty).Trim();
        if (!allowPathRename)
        {
            normalizedYaml = LockYamlSlugToPath(normalizedRelativePath, normalizedYaml);
        }

        var normalizedBody = (markdown ?? string.Empty).Replace("\r\n", "\n");
        var content = string.IsNullOrWhiteSpace(normalizedYaml)
            ? normalizedBody
            : $"---\n{normalizedYaml}\n---\n{normalizedBody}";

        if (allowPathRename &&
            TryCreateMovedRelativePath(normalizedRelativePath, normalizedYaml, out var movedRelativePath))
        {
            var movedFullPath = ResolveContentPathForMovedFile(movedRelativePath);
            MoveContentDirectory(fullPath, movedFullPath);
            fullPath = movedFullPath;
            normalizedRelativePath = movedRelativePath;
        }

        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把编辑器临时草稿第一次落到内容 workspace，并把临时资产迁入最终内容目录。</summary>
    public async Task<EditableContentFile> CreateFromDraftAsync(
        ContentKind kind,
        string yaml,
        string markdown,
        string? sourceAssetsDirectory,
        CancellationToken cancellationToken = default)
    {
        if (kind is not (ContentKind.Post or ContentKind.Page or ContentKind.Work))
        {
            throw new InvalidOperationException("这个内容类型暂时不能从编辑器临时草稿落盘。");
        }

        var now = _time.GetUtcNow();
        var normalizedYaml = (yaml ?? string.Empty).Trim();
        var slug = ReadSlugForNewContent(kind, normalizedYaml);
        var relativePath = CreateRelativePathForNewContent(kind, slug, now);
        await SaveNewAsync(relativePath, normalizedYaml, markdown, cancellationToken).ConfigureAwait(false);
        MoveDraftAssets(sourceAssetsDirectory, ResolveContentFile(relativePath));
        return await ReadAsync(relativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>创建一个已经确定路径的语言 variant 文件；调用方负责决定语言、group 与来源关系。</summary>
    public async Task<EditableContentFile> CreateLanguageVariantAsync(
        string relativePath,
        string yaml,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeContentRelativePath(relativePath);
        if (!IsLanguageVariantRelativePath(normalizedRelativePath))
        {
            throw new InvalidOperationException("语言版本必须写入 index.{language}.md 文件。");
        }

        await SaveNewAsync(normalizedRelativePath, yaml, markdown, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>规范化候选路径标识，并按最终 URL 作用域检查是否已被其它内容占用。</summary>
    public ContentSlugValidationResult ValidateUrlSlug(ContentKind kind, string? currentRelativePath, string? candidate)
    {
        var slug = ContentSlug.Normalize(candidate);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return ContentSlugValidationResult.Unavailable(slug, "路径标识不能为空。", ContentSlugValidationIssue.Empty);
        }

        var currentFullPath = string.IsNullOrWhiteSpace(currentRelativePath)
            ? null
            : ResolveContentFile(currentRelativePath);
        return kind switch
        {
            ContentKind.Page => ValidatePageSlug(currentFullPath, slug),
            ContentKind.Post => ValidateYearScopedSlug(currentRelativePath, currentFullPath, _layout.Workspace.PostsDirectory, "posts", "文章", slug),
            ContentKind.Work => ValidateYearScopedSlug(currentRelativePath, currentFullPath, _layout.Workspace.WorksDirectory, "works", "作品", slug),
            _ => ContentSlugValidationResult.Available(slug),
        };
    }

    /// <summary>校验尚未落盘的新内容 slug；Post/Work 使用当前年份作为最终 URL 作用域。</summary>
    public ContentSlugValidationResult ValidateNewUrlSlug(ContentKind kind, string? candidate, string? year = null)
    {
        var slug = ContentSlug.Normalize(candidate);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return ContentSlugValidationResult.Unavailable(slug, "路径标识不能为空。", ContentSlugValidationIssue.Empty);
        }

        year ??= _time.GetUtcNow().Year.ToString(CultureInfo.InvariantCulture);
        return kind switch
        {
            ContentKind.Page => ValidatePageSlug(currentFullPath: null, slug),
            ContentKind.Post => ValidateNewYearScopedSlug(_layout.Workspace.PostsDirectory, year, "文章", slug),
            ContentKind.Work => ValidateNewYearScopedSlug(_layout.Workspace.WorksDirectory, year, "作品", slug),
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
        var contentDirectory = Path.GetDirectoryName(fullPath);
        if (IsIndexMarkdown(fullPath) && contentDirectory is not null)
        {
            Directory.Delete(contentDirectory, recursive: true);
            PruneEmptyDirectories(Path.GetDirectoryName(contentDirectory));
            return Task.CompletedTask;
        }

        File.Delete(fullPath);
        PruneEmptyDirectories(contentDirectory);
        return Task.CompletedTask;
    }

    /// <summary>把内容相对路径转成编辑页 URL。</summary>
    public static string EditUrl(string relativePath)
        => "/Admin/Content/Edit?path=" + Uri.EscapeDataString(relativePath.Replace('\\', '/'));

    private string ResolveContentFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = NormalizeContentRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar);
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
        var normalized = NormalizeContentRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar);
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

    private string ResolveContentPathForMovedFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = NormalizeContentRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar);
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

        var targetDirectory = Path.GetDirectoryName(fullPath)!;
        if (Directory.Exists(targetDirectory) || File.Exists(fullPath))
        {
            throw new InvalidOperationException("目标内容目录已经存在。");
        }

        return fullPath;
    }

    private static string NormalizeContentRelativePath(string relativePath)
        => relativePath.Replace('\\', '/').TrimStart('/');

    private static string FormatDateTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private string ReadSlugForNewContent(ContentKind kind, string yaml)
    {
        var root = ParseYaml(yaml);
        var fallback = kind switch
        {
            ContentKind.Page => "new-page",
            ContentKind.Work => "new-work",
            _ => "new-post",
        };
        var slug = ContentSlug.Normalize(ReadYamlString(root, "slug") ?? fallback);
        var validation = ValidateNewUrlSlug(kind, slug);
        if (!validation.IsAvailable)
        {
            throw new InvalidOperationException(validation.Reason ?? "路径标识不可用。");
        }

        return validation.Slug;
    }

    private static string CreateRelativePathForNewContent(ContentKind kind, string slug, DateTimeOffset now)
    {
        var year = now.Year.ToString(CultureInfo.InvariantCulture);
        return kind switch
        {
            ContentKind.Page => Path.Combine("pages", slug, "index.md"),
            ContentKind.Work => Path.Combine("works", year, slug, "index.md"),
            _ => Path.Combine("posts", year, slug, "index.md"),
        };
    }

    private static bool TryCreateMovedRelativePath(string currentRelativePath, string yaml, out string movedRelativePath)
    {
        movedRelativePath = currentRelativePath;
        if (!TryReadSlugManagedRelativePath(currentRelativePath, out var parts, out _))
        {
            return false;
        }

        YamlMappingNode? root;
        try
        {
            root = ParseYaml(yaml);
        }
        catch (YamlException)
        {
            return false;
        }

        var slug = ContentSlug.Normalize(ReadYamlString(root, "slug"));
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        movedRelativePath = parts[0].ToLowerInvariant() switch
        {
            "pages" when parts.Length == 3 => $"pages/{slug}/index.md",
            "posts" when parts.Length == 4 => $"posts/{parts[1]}/{slug}/index.md",
            "works" when parts.Length == 4 => $"works/{parts[1]}/{slug}/index.md",
            _ => currentRelativePath,
        };
        return !string.Equals(currentRelativePath, movedRelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string LockYamlSlugToPath(string currentRelativePath, string yaml)
    {
        if (!TryReadSlugManagedRelativePath(currentRelativePath, out _, out var pathSlug) ||
            string.IsNullOrWhiteSpace(yaml))
        {
            return yaml;
        }

        try
        {
            var root = ParseYaml(yaml) ?? new YamlMappingNode();
            root.Children[new YamlScalarNode("slug")] = new YamlScalarNode(pathSlug);
            var stream = new YamlStream(new YamlDocument(root));
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            stream.Save(writer, assignAnchors: false);
            return writer.ToString().Trim();
        }
        catch (YamlException)
        {
            return yaml;
        }
    }

    private static bool TryReadSlugManagedRelativePath(
        string currentRelativePath,
        out string[] parts,
        out string slug)
    {
        parts = currentRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        slug = string.Empty;
        if (parts.Length < 3 || !string.Equals(parts[^1], "index.md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        slug = parts[0].ToLowerInvariant() switch
        {
            "pages" when parts.Length == 3 => parts[1],
            "posts" when parts.Length == 4 => parts[2],
            "works" when parts.Length == 4 => parts[2],
            _ => string.Empty,
        };
        return !string.IsNullOrWhiteSpace(slug);
    }

    private void MoveContentDirectory(string currentContentFile, string movedContentFile)
    {
        var currentDirectory = Path.GetDirectoryName(currentContentFile)!;
        var movedDirectory = Path.GetDirectoryName(movedContentFile)!;
        Directory.CreateDirectory(Path.GetDirectoryName(movedDirectory)!);
        Directory.Move(currentDirectory, movedDirectory);
        PruneEmptyDirectories(Path.GetDirectoryName(currentDirectory));
    }

    private static void MoveDraftAssets(string? sourceAssetsDirectory, string finalContentFile)
    {
        if (string.IsNullOrWhiteSpace(sourceAssetsDirectory) || !Directory.Exists(sourceAssetsDirectory))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(sourceAssetsDirectory).Any())
        {
            return;
        }

        var finalAssetsDirectory = Path.Combine(Path.GetDirectoryName(finalContentFile)!, "assets");
        if (Directory.Exists(finalAssetsDirectory))
        {
            Directory.Delete(finalAssetsDirectory, recursive: true);
        }

        Directory.Move(sourceAssetsDirectory, finalAssetsDirectory);
    }

    private static bool IsIndexMarkdown(string fullPath)
        => string.Equals(Path.GetFileName(fullPath), "index.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsLanguageVariantRelativePath(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath.Replace('\\', '/'));
        return fileName.StartsWith("index.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fileName, "index.md", StringComparison.OrdinalIgnoreCase);
    }

    private ContentSlugValidationResult ValidatePageSlug(string? currentFullPath, string slug)
    {
        if (ReservedTopLevelPageSlugs.Contains(slug))
        {
            return ContentSlugValidationResult.Unavailable(slug, "这个路径标识会与系统路由冲突。", ContentSlugValidationIssue.ReservedRoute);
        }

        if (IsSlugTaken(_layout.Workspace.PagesDirectory, currentFullPath, slug))
        {
            return ContentSlugValidationResult.Unavailable(slug, "这个路径标识已经被其它页面使用。", ContentSlugValidationIssue.PageTaken);
        }

        return ContentSlugValidationResult.Available(slug);
    }

    private static ContentSlugValidationResult ValidateYearScopedSlug(
        string? currentRelativePath,
        string? currentFullPath,
        string contentRoot,
        string expectedKindDirectory,
        string kindLabel,
        string slug)
    {
        if (string.IsNullOrWhiteSpace(currentRelativePath) ||
            !TryReadYearFromRelativePath(currentRelativePath, expectedKindDirectory, out var year))
        {
            return ContentSlugValidationResult.Unavailable(slug, "当前内容路径无法判断发布年份。", ContentSlugValidationIssue.YearUnavailable);
        }

        var yearRoot = Path.Combine(contentRoot, year);
        return IsSlugTaken(yearRoot, currentFullPath, slug)
            ? ContentSlugValidationResult.Unavailable(
                slug,
                $"这个路径标识已经被同一年份的其它{kindLabel}使用。",
                ContentSlugValidationIssue.YearScopedTaken)
            : ContentSlugValidationResult.Available(slug);
    }

    private static ContentSlugValidationResult ValidateNewYearScopedSlug(
        string contentRoot,
        string year,
        string kindLabel,
        string slug)
    {
        var yearRoot = Path.Combine(contentRoot, year);
        return IsSlugTaken(yearRoot, currentFullPath: null, slug)
            ? ContentSlugValidationResult.Unavailable(
                slug,
                $"这个路径标识已经被同一年份的其它{kindLabel}使用。",
                ContentSlugValidationIssue.YearScopedTaken)
            : ContentSlugValidationResult.Available(slug);
    }

    private static bool IsSlugTaken(string root, string? currentFullPath, string slug)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }

        var current = string.IsNullOrWhiteSpace(currentFullPath) ? null : Path.GetFullPath(currentFullPath);
        foreach (var indexFile in Directory.EnumerateFiles(root, "index.md", SearchOption.AllDirectories))
        {
            if (current is not null && string.Equals(Path.GetFullPath(indexFile), current, StringComparison.OrdinalIgnoreCase))
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
