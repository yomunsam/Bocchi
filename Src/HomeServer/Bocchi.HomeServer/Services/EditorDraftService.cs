using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Bocchi.ContentModel;
using Bocchi.Workspace;
using Bocchi.Workspace.Content;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 管理尚未落入内容 workspace 的编辑器临时草稿。它只保存 Home Server 编辑会话状态，
/// 用户明确暂存或发布前不会创建正式内容文件。
/// </summary>
public sealed partial class EditorDraftService
{
    private const string ContentFileName = "content.md";
    private const string AssetsDirectoryName = "assets";
    private const string NoteDraftId = "note";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly BocchiDataLayout _layout;
    private readonly MarkdownPipeline _markdown;
    private readonly TimeProvider _time;

    /// <summary>构造编辑器临时草稿服务。</summary>
    public EditorDraftService(BocchiDataLayout layout, MarkdownPipeline markdown, TimeProvider time)
    {
        _layout = layout;
        _markdown = markdown;
        _time = time;
    }

    /// <summary>创建一个新的临时草稿会话；Note 预留为单例会话。</summary>
    public async Task<EditorDraftSession> CreateAsync(ContentKind kind, CancellationToken cancellationToken = default)
    {
        if (kind is not (ContentKind.Post or ContentKind.Page or ContentKind.Work or ContentKind.Note))
        {
            throw new InvalidOperationException("这个内容类型暂时没有编辑器临时草稿。");
        }

        var draftId = kind == ContentKind.Note
            ? NoteDraftId
            : $"{KindSlug(kind)}-{Guid.NewGuid():N}";
        var directory = ResolveDraftDirectory(draftId, allowNew: true);
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, AssetsDirectoryName));

        var now = _time.GetUtcNow();
        var metadata = new EditorDraftMetadata(kind, now, now);
        await WriteMetadataAsync(directory, metadata, cancellationToken).ConfigureAwait(false);

        if (!File.Exists(ContentPath(directory)))
        {
            var (yaml, markdown) = CreateInitialContent(kind, now);
            await WriteContentAsync(directory, yaml, markdown, cancellationToken).ConfigureAwait(false);
        }

        return await ReadAsync(draftId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取一个临时草稿会话，并渲染 Markdown 预览。</summary>
    public async Task<EditorDraftSession> ReadAsync(string draftId, CancellationToken cancellationToken = default)
    {
        var directory = ResolveDraftDirectory(draftId, allowNew: false);
        var metadata = await ReadMetadataAsync(directory, cancellationToken).ConfigureAwait(false);
        var content = await File.ReadAllTextAsync(ContentPath(directory), cancellationToken).ConfigureAwait(false);
        var split = FrontmatterParser.Split(content);
        return new EditorDraftSession(
            draftId,
            metadata.Kind,
            split.Yaml,
            split.Body,
            _markdown.RenderHtml(split.Body),
            Path.Combine(directory, AssetsDirectoryName),
            metadata.CreatedAt,
            metadata.UpdatedAt);
    }

    /// <summary>保存临时草稿的编辑缓冲区；未来粘贴图片时可先保存正文再写入资产。</summary>
    public async Task SaveBufferAsync(string draftId, string yaml, string markdown, CancellationToken cancellationToken = default)
    {
        var directory = ResolveDraftDirectory(draftId, allowNew: false);
        var metadata = await ReadMetadataAsync(directory, cancellationToken).ConfigureAwait(false);
        metadata = metadata with { UpdatedAt = _time.GetUtcNow() };
        await WriteMetadataAsync(directory, metadata, cancellationToken).ConfigureAwait(false);
        await WriteContentAsync(directory, yaml, markdown, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把一份上传或粘贴资产写入临时草稿，并返回 Markdown 可直接引用的相对路径。</summary>
    public async Task<string> MoveAssetToDraftAsync(
        string draftId,
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var directory = ResolveDraftDirectory(draftId, allowNew: false);
        var assetsDirectory = Path.Combine(directory, AssetsDirectoryName);
        Directory.CreateDirectory(assetsDirectory);

        var safeName = CreateSafeAssetFileName(fileName);
        var finalName = CreateUniqueAssetName(assetsDirectory, safeName);
        var fullPath = Path.Combine(assetsDirectory, finalName);
        await using var output = File.Create(fullPath);
        await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        var metadata = await ReadMetadataAsync(directory, cancellationToken).ConfigureAwait(false);
        await WriteMetadataAsync(directory, metadata with { UpdatedAt = _time.GetUtcNow() }, cancellationToken)
            .ConfigureAwait(false);
        return $"{AssetsDirectoryName}/{finalName}";
    }

    /// <summary>删除临时草稿会话；正式内容落盘后也会调用它清理 state。</summary>
    public Task DeleteAsync(string draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = ResolveDraftDirectory(draftId, allowNew: false);
        Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>把临时草稿 id 转为编辑器 URL。</summary>
    public static string EditUrl(string draftId)
        => "/Admin/Content/Edit?draft=" + Uri.EscapeDataString(draftId);

    private string ResolveDraftDirectory(string draftId, bool allowNew)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        if (!DraftIdPattern().IsMatch(draftId))
        {
            throw new InvalidOperationException("临时草稿标识无效。");
        }

        var root = Path.GetFullPath(_layout.EditorDraftsDirectory);
        Directory.CreateDirectory(root);
        var fullPath = Path.GetFullPath(Path.Combine(root, draftId));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("临时草稿必须位于 editor-drafts 目录内。");
        }

        if (!allowNew && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到临时草稿。", fullPath);
        }

        return fullPath;
    }

    private static string ContentPath(string directory)
        => Path.Combine(directory, ContentFileName);

    private static string MetadataPath(string directory)
        => Path.Combine(directory, "session.json");

    private static async Task<EditorDraftMetadata> ReadMetadataAsync(string directory, CancellationToken cancellationToken)
    {
        var path = MetadataPath(directory);
        await using var stream = File.OpenRead(path);
        var metadata = await JsonSerializer.DeserializeAsync<EditorDraftMetadata>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return metadata ?? throw new InvalidOperationException("临时草稿元数据为空。");
    }

    private static async Task WriteMetadataAsync(
        string directory,
        EditorDraftMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(MetadataPath(directory));
        await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteContentAsync(
        string directory,
        string yaml,
        string markdown,
        CancellationToken cancellationToken)
    {
        var normalizedYaml = (yaml ?? string.Empty).Trim();
        var normalizedBody = (markdown ?? string.Empty).Replace("\r\n", "\n");
        var content = string.IsNullOrWhiteSpace(normalizedYaml)
            ? normalizedBody
            : $"---\n{normalizedYaml}\n---\n{normalizedBody}";
        await File.WriteAllTextAsync(ContentPath(directory), content, cancellationToken).ConfigureAwait(false);
    }

    private static (string Yaml, string Markdown) CreateInitialContent(ContentKind kind, DateTimeOffset now)
        => kind switch
        {
            ContentKind.Page => (
                """
                title: New Page
                slug: new-page
                status: draft
                template: normal
                order: 0
                showInNavigation: false
                """,
                string.Empty),
            ContentKind.Work => (
                """
                title: New Work
                slug: new-work
                status: draft
                role:
                period:
                stack: []
                summary:
                featured: false
                """,
                "# New Work\n\n"),
            ContentKind.Note => (
                $"""
                status: draft
                updatedAt: {FormatDateTime(now)}
                """,
                string.Empty),
            _ => (
                $"""
                title: New Post
                slug: new-post
                status: draft
                updatedAt: {FormatDateTime(now)}
                category:
                tags: []
                summary:
                """,
                "# New Post\n\n"),
        };

    /// <summary>把用户上传文件名归一为 assets/ 下可保存的安全文件名。</summary>
    internal static string CreateSafeAssetFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        stem = ContentSlug.Normalize(stem);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "asset";
        }

        extension = AssetExtensionPattern().IsMatch(extension) ? extension.ToLowerInvariant() : string.Empty;
        return stem + extension;
    }

    /// <summary>在目标目录中生成不覆盖已有文件的资产文件名。</summary>
    internal static string CreateUniqueAssetName(string directory, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = fileName;
        var index = 2;
        while (File.Exists(Path.Combine(directory, candidate)))
        {
            candidate = $"{stem}-{index.ToString(CultureInfo.InvariantCulture)}{extension}";
            index++;
        }

        return candidate;
    }

    private static string KindSlug(ContentKind kind)
        => kind switch
        {
            ContentKind.Page => "page",
            ContentKind.Work => "work",
            ContentKind.Note => "note",
            _ => "post",
        };

    private static string FormatDateTime(DateTimeOffset value)
        => value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DraftIdPattern();

    [GeneratedRegex("^\\.[a-z0-9]{1,12}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetExtensionPattern();
}

/// <summary>编辑器临时草稿的读取结果。</summary>
/// <param name="DraftId">临时草稿 id。</param>
/// <param name="Kind">内容类型。</param>
/// <param name="Yaml">frontmatter YAML，不含分隔符。</param>
/// <param name="Markdown">Markdown 正文。</param>
/// <param name="PreviewHtml">使用统一 Markdig pipeline 渲染的预览 HTML。</param>
/// <param name="AssetsDirectory">临时资产目录。</param>
/// <param name="CreatedAt">草稿创建时间。</param>
/// <param name="UpdatedAt">草稿最后更新时间。</param>
public sealed record EditorDraftSession(
    string DraftId,
    ContentKind Kind,
    string Yaml,
    string Markdown,
    string PreviewHtml,
    string AssetsDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>临时草稿 session.json 的最小元数据。</summary>
/// <param name="Kind">内容类型。</param>
/// <param name="CreatedAt">草稿创建时间。</param>
/// <param name="UpdatedAt">草稿最后更新时间。</param>
internal sealed record EditorDraftMetadata(ContentKind Kind, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
