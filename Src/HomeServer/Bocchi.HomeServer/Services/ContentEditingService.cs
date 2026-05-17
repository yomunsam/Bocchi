using Bocchi.Workspace;
using Bocchi.Workspace.Content;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Admin Markdown 编辑服务。它只读写内容 workspace 文件，不把正文复制进 Home Server 数据库。
/// </summary>
public sealed class ContentEditingService
{
    private readonly BocchiDataLayout _layout;
    private readonly MarkdownPipeline _markdown;

    /// <summary>构造内容编辑服务。</summary>
    public ContentEditingService(BocchiDataLayout layout, MarkdownPipeline markdown)
    {
        _layout = layout;
        _markdown = markdown;
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
