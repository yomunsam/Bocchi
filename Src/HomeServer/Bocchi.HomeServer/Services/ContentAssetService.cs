using System.Globalization;
using System.Text.RegularExpressions;

using Bocchi.ContentModel;
using Bocchi.Workspace;
using Bocchi.Workspace.Content;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 内容资产服务。它只管理当前草稿或目录型内容组下的 <c>assets/</c> 文件，
/// workspace 仍然是资产事实来源，不引入数据库媒体表。
/// </summary>
public sealed partial class ContentAssetService
{
    private const string AssetsDirectoryName = "assets";
    private const string AssetsRelativePrefix = "assets/";

    private static readonly IReadOnlyDictionary<string, ContentAssetDescriptor> AllowedAssetTypes =
        new Dictionary<string, ContentAssetDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new("image/jpeg", ContentAssetCategory.Image),
            [".jpeg"] = new("image/jpeg", ContentAssetCategory.Image),
            [".png"] = new("image/png", ContentAssetCategory.Image),
            [".gif"] = new("image/gif", ContentAssetCategory.Image),
            [".webp"] = new("image/webp", ContentAssetCategory.Image),
            [".avif"] = new("image/avif", ContentAssetCategory.Image),
            [".pdf"] = new("application/pdf", ContentAssetCategory.Attachment),
            [".txt"] = new("text/plain", ContentAssetCategory.Attachment),
            [".md"] = new("text/markdown", ContentAssetCategory.Attachment),
        };

    private readonly BocchiDataLayout _layout;
    private readonly EditorDraftService _drafts;
    private readonly MarkdownPipeline _markdown;

    /// <summary>构造内容资产服务。</summary>
    public ContentAssetService(BocchiDataLayout layout, EditorDraftService drafts, MarkdownPipeline markdown)
    {
        _layout = layout;
        _drafts = drafts;
        _markdown = markdown;
    }

    /// <summary>枚举草稿 <c>assets/</c> 下的文件，并根据当前编辑缓冲判断是否被引用。</summary>
    public async Task<IReadOnlyList<ContentAssetView>> ListDraftAssetsAsync(
        string draftId,
        string yaml,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var session = await _drafts.ReadAsync(draftId, cancellationToken).ConfigureAwait(false);
        EnsureSupportedDraftKind(session.Kind);
        var referenced = CollectReferencedAssets(yaml, markdown);
        return EnumerateAssets(session.AssetsDirectory, referenced);
    }

    /// <summary>枚举已保存目录型内容组 <c>assets/</c> 下的文件，并根据同组所有语言 variant 判断是否被引用。</summary>
    public async Task<IReadOnlyList<ContentAssetView>> ListContentAssetsAsync(
        string contentRelativePath,
        string yaml,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contentFile = ResolveContentFile(contentRelativePath);
        var assetsDirectory = ResolveContentAssetsDirectoryForFile(contentFile);
        var referenced = await CollectContentGroupReferencedAssetsAsync(contentFile, yaml, markdown, cancellationToken)
            .ConfigureAwait(false);
        return EnumerateAssets(assetsDirectory, referenced);
    }

    /// <summary>把上传文件写入草稿 <c>assets/</c>，返回最终安全文件名与相对引用路径。</summary>
    public async Task<ContentAssetUploadResult> UploadDraftAssetAsync(
        string draftId,
        Stream stream,
        string fileName,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var descriptor = ValidateUploadFileName(fileName, contentType);
        var session = await _drafts.ReadAsync(draftId, cancellationToken).ConfigureAwait(false);
        EnsureSupportedDraftKind(session.Kind);

        var relativePath = await _drafts.MoveAssetToDraftAsync(draftId, stream, fileName, cancellationToken).ConfigureAwait(false);
        var fullPath = ResolveAssetFile(session.AssetsDirectory, relativePath);
        return CreateUploadResult(fullPath, relativePath, descriptor);
    }

    /// <summary>把上传文件写入已保存目录型内容组 <c>assets/</c>，返回最终安全文件名与相对引用路径。</summary>
    public async Task<ContentAssetUploadResult> UploadContentAssetAsync(
        string contentRelativePath,
        Stream stream,
        string fileName,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var descriptor = ValidateUploadFileName(fileName, contentType);
        var assetsDirectory = ResolveContentAssetsDirectory(contentRelativePath);
        Directory.CreateDirectory(assetsDirectory);

        var safeName = EditorDraftService.CreateSafeAssetFileName(fileName);
        var finalName = EditorDraftService.CreateUniqueAssetName(assetsDirectory, safeName);
        var fullPath = Path.Combine(assetsDirectory, finalName);
        await using (var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        return CreateUploadResult(fullPath, AssetsRelativePrefix + finalName, descriptor);
    }

    /// <summary>删除草稿中的未引用资产；若当前编辑缓冲仍引用该文件则拒绝删除。</summary>
    public async Task DeleteDraftAssetAsync(
        string draftId,
        string assetRelativePath,
        string yaml,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var session = await _drafts.ReadAsync(draftId, cancellationToken).ConfigureAwait(false);
        EnsureSupportedDraftKind(session.Kind);
        DeleteAsset(session.AssetsDirectory, assetRelativePath, CollectReferencedAssets(yaml, markdown));
    }

    /// <summary>删除已保存目录型内容组中的未引用资产；若同组任一语言 variant 仍引用该文件则拒绝删除。</summary>
    public async Task DeleteContentAssetAsync(
        string contentRelativePath,
        string assetRelativePath,
        string yaml,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contentFile = ResolveContentFile(contentRelativePath);
        var assetsDirectory = ResolveContentAssetsDirectoryForFile(contentFile);
        var referenced = await CollectContentGroupReferencedAssetsAsync(contentFile, yaml, markdown, cancellationToken)
            .ConfigureAwait(false);
        DeleteAsset(assetsDirectory, assetRelativePath, referenced);
    }

    private static IReadOnlyList<ContentAssetView> EnumerateAssets(
        string assetsDirectory,
        IReadOnlySet<string> referenced)
    {
        if (!Directory.Exists(assetsDirectory))
        {
            return [];
        }

        var list = new List<ContentAssetView>();
        foreach (var file in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = AssetsRelativePrefix +
                Path.GetRelativePath(assetsDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
            var descriptor = GetDescriptorForFile(file);
            var info = new FileInfo(file);
            list.Add(new ContentAssetView(
                Path.GetFileName(file),
                relativePath,
                info.Length,
                descriptor.ContentType,
                descriptor.Category,
                referenced.Contains(relativePath)));
        }

        return list;
    }

    private static void DeleteAsset(
        string assetsDirectory,
        string assetRelativePath,
        IReadOnlySet<string> referenced)
    {
        var normalized = NormalizeAssetPathOrThrow(assetRelativePath);
        if (referenced.Contains(normalized))
        {
            throw new InvalidOperationException("资产仍被当前内容引用，不能删除。");
        }

        var fullPath = ResolveAssetFile(assetsDirectory, normalized);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("资产文件不存在。", normalized);
        }

        File.Delete(fullPath);
    }

    private HashSet<string> CollectReferencedAssets(string yaml, string markdown)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFrontmatterReferences(yaml, referenced);
        CollectMarkdownReferences(markdown, referenced);
        return referenced;
    }

    private async Task<HashSet<string>> CollectContentGroupReferencedAssetsAsync(
        string currentContentFile,
        string currentYaml,
        string currentMarkdown,
        CancellationToken cancellationToken)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentFullPath = Path.GetFullPath(currentContentFile);
        var contentDirectory = Path.GetDirectoryName(currentFullPath)!;

        // 同一 localization group 的语言版本共享目录级 assets/；删除判断必须看完整内容组。
        foreach (var variantFile in Directory.EnumerateFiles(contentDirectory, "index*.md", SearchOption.TopDirectoryOnly)
                     .Where(IsDirectoryContentFile)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFullPath(variantFile), currentFullPath, StringComparison.OrdinalIgnoreCase))
            {
                CollectFrontmatterReferences(currentYaml, referenced);
                CollectMarkdownReferences(currentMarkdown, referenced);
                continue;
            }

            var raw = await File.ReadAllTextAsync(variantFile, cancellationToken).ConfigureAwait(false);
            var split = FrontmatterParser.Split(raw);
            CollectFrontmatterReferences(split.Yaml, referenced);
            CollectMarkdownReferences(split.Body, referenced);
        }

        return referenced;
    }

    private static void CollectFrontmatterReferences(string yaml, HashSet<string> referenced)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return;
        }

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (YamlException)
        {
            return;
        }

        if (root is null)
        {
            return;
        }

        TryAddAssetReference(ReadScalar(root, "cover"), referenced);
        if (!root.Children.TryGetValue(new YamlScalarNode("media"), out var mediaNode) ||
            mediaNode is not YamlSequenceNode media)
        {
            return;
        }

        foreach (var item in media.Children)
        {
            switch (item)
            {
                case YamlScalarNode scalar:
                    TryAddAssetReference(scalar.Value, referenced);
                    break;
                case YamlMappingNode mapping:
                    TryAddAssetReference(ReadScalar(mapping, "path"), referenced);
                    break;
            }
        }
    }

    private void CollectMarkdownReferences(string markdown, HashSet<string> referenced)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        foreach (var media in _markdown.ExtractMediaReferences(markdown))
        {
            TryAddAssetReference(media.Path, referenced);
        }

        foreach (Match match in MarkdownLinkRegex().Matches(markdown))
        {
            TryAddAssetReference(match.Groups["target"].Value, referenced);
        }

        foreach (Match match in HtmlImageRegex().Matches(markdown))
        {
            var target = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Success
                    ? match.Groups["single"].Value
                    : match.Groups["bare"].Value;
            TryAddAssetReference(target, referenced);
        }
    }

    private static ContentAssetDescriptor ValidateUploadFileName(string fileName, string? contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.Contains('\0') ||
            fileName.Contains('/', StringComparison.Ordinal) ||
            fileName.Contains('\\', StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("资产文件名不能包含路径。");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedAssetTypes.TryGetValue(extension, out var descriptor))
        {
            throw new InvalidOperationException("不支持的资产文件类型。");
        }

        if (contentType?.Contains("svg", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException("不支持的资产文件类型。");
        }

        return descriptor;
    }

    private string ResolveContentAssetsDirectory(string contentRelativePath)
        => ResolveContentAssetsDirectoryForFile(ResolveContentFile(contentRelativePath));

    private static string ResolveContentAssetsDirectoryForFile(string contentFile)
    {
        if (!IsDirectoryContentFile(contentFile))
        {
            throw new InvalidOperationException("当前只支持目录型内容的 assets/，单文件 Note 暂不开放内容资产。");
        }

        return Path.Combine(Path.GetDirectoryName(contentFile)!, AssetsDirectoryName);
    }

    private string ResolveContentFile(string contentRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRelativePath);
        if (Path.IsPathRooted(contentRelativePath))
        {
            throw new InvalidOperationException("内容路径必须是 workspace 相对路径。");
        }

        var normalized = contentRelativePath.Replace('\\', '/').TrimStart('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            throw new InvalidOperationException("内容路径不能包含上级目录跳转。");
        }

        var root = Path.GetFullPath(_layout.WorkspaceRoot);
        var fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!IsUnderDirectory(fullPath, root))
        {
            throw new InvalidOperationException("内容路径必须位于 workspace 内。");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("内容文件不存在。", normalized);
        }

        return fullPath;
    }

    private static string NormalizeAssetPathOrThrow(string assetRelativePath)
    {
        if (!TryNormalizeAssetReference(assetRelativePath, out var normalized))
        {
            throw new InvalidOperationException("资产路径必须位于当前内容的 assets/ 下。");
        }

        return normalized;
    }

    private static void TryAddAssetReference(string? raw, HashSet<string> referenced)
    {
        if (TryNormalizeAssetReference(raw, out var normalized))
        {
            referenced.Add(normalized);
        }
    }

    private static bool TryNormalizeAssetReference(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim().Trim('<', '>');
        var queryIndex = value.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            value = value[..queryIndex];
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out _) || value.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            value = Uri.UnescapeDataString(value).Replace('\\', '/');
        }
        catch (UriFormatException)
        {
            return false;
        }
        while (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !string.Equals(parts[0], AssetsDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            parts.Any(part => part is "." or ".."))
        {
            return false;
        }

        normalized = string.Join('/', parts);
        return true;
    }

    private static string ResolveAssetFile(string assetsDirectory, string assetRelativePath)
    {
        var normalized = NormalizeAssetPathOrThrow(assetRelativePath);
        var relativeInsideAssets = normalized[AssetsRelativePrefix.Length..];
        var root = Path.GetFullPath(assetsDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativeInsideAssets));
        if (!IsUnderDirectory(fullPath, root))
        {
            throw new InvalidOperationException("资产路径必须位于当前内容的 assets/ 下。");
        }

        return fullPath;
    }

    private static ContentAssetUploadResult CreateUploadResult(
        string fullPath,
        string relativePath,
        ContentAssetDescriptor descriptor)
    {
        var info = new FileInfo(fullPath);
        return new ContentAssetUploadResult(
            info.Name,
            relativePath,
            info.Length,
            descriptor.ContentType,
            descriptor.Category);
    }

    private static ContentAssetDescriptor GetDescriptorForFile(string file)
    {
        var extension = Path.GetExtension(file);
        return AllowedAssetTypes.TryGetValue(extension, out var descriptor)
            ? descriptor
            : new("application/octet-stream", ContentAssetCategory.Attachment);
    }

    private static void EnsureSupportedDraftKind(ContentKind kind)
    {
        if (kind == ContentKind.Note)
        {
            throw new InvalidOperationException("Note 资产需要目录型 Note 后再开放。");
        }
    }

    private static bool IsDirectoryContentFile(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        return string.Equals(fileName, "index.md", StringComparison.OrdinalIgnoreCase) ||
            (fileName.StartsWith("index.", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderDirectory(string fullPath, string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        var candidate = Path.GetFullPath(fullPath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadScalar(YamlMappingNode mapping, string key)
        => mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) &&
            node is YamlScalarNode scalar &&
            !string.IsNullOrWhiteSpace(scalar.Value)
            ? scalar.Value.Trim()
            : null;

    [GeneratedRegex(@"!?\[[^\]]*\]\(\s*(?<target><[^>]+>|[^)\s]+)(?:\s+[""'][^""']*[""'])?\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("""<img\b[^>]*\bsrc\s*=\s*(?:"(?<double>[^"]+)"|'(?<single>[^']+)'|(?<bare>[^\s>]+))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlImageRegex();

    private sealed record ContentAssetDescriptor(string ContentType, ContentAssetCategory Category);
}

/// <summary>内容资产列表项，供后台 UI 直接消费。</summary>
/// <param name="FileName">最终文件名。</param>
/// <param name="RelativePath">写入 Markdown/frontmatter 时使用的 <c>assets/...</c> 相对路径。</param>
/// <param name="Size">文件大小，单位 byte。</param>
/// <param name="ContentType">根据扩展名归一化后的 MIME 类型。</param>
/// <param name="Category">资产类型分类。</param>
/// <param name="Referenced">当前编辑缓冲是否引用该资产。</param>
public sealed record ContentAssetView(
    string FileName,
    string RelativePath,
    long Size,
    string ContentType,
    ContentAssetCategory Category,
    bool Referenced);

/// <summary>内容资产上传结果。</summary>
/// <param name="FileName">最终文件名。</param>
/// <param name="RelativePath">写入 Markdown/frontmatter 时使用的 <c>assets/...</c> 相对路径。</param>
/// <param name="Size">文件大小，单位 byte。</param>
/// <param name="ContentType">根据扩展名归一化后的 MIME 类型。</param>
/// <param name="Category">资产类型分类。</param>
public sealed record ContentAssetUploadResult(
    string FileName,
    string RelativePath,
    long Size,
    string ContentType,
    ContentAssetCategory Category);

/// <summary>内容资产面向 UI 的粗分类。</summary>
public enum ContentAssetCategory
{
    /// <summary>图片资源。</summary>
    Image,

    /// <summary>基础附件资源。</summary>
    Attachment,
}
