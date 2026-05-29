using System.Net;
using System.Text.RegularExpressions;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Components.Ui;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>ContentEditor 的内容资产上传与编辑器预览 URL 处理。</summary>
public partial class ContentEditor
{
    /// <summary>处理 Markdown editor 粘贴图片：写入当前内容组 assets/，并返回要插入的图片 Markdown。</summary>
    private async Task<MarkdownEditorAssetUploadResult> UploadPastedImageAsync(MarkdownEditorPastedImageRequest request)
    {
        if (!HasEditorDocument || !request.Content.CanRead || CurrentKind() is not (ContentKind.Post or ContentKind.Page or ContentKind.Work))
        {
            return MarkdownEditorAssetUploadResult.Failure(Text("contentEditor.asset.pasteUnavailable"));
        }

        if (!LooksLikeImageUpload(request))
        {
            return MarkdownEditorAssetUploadResult.Failure(Text("contentEditor.asset.imageOnly"));
        }

        try
        {
            var fileName = ResolvePastedImageFileName(request);
            var uploaded = IsUnsavedDraft && _draftSession is not null
                ? await ContentAssets.UploadDraftAssetAsync(
                    _draftSession.DraftId,
                    request.Content,
                    fileName,
                    request.ContentType)
                : await ContentAssets.UploadContentAssetAsync(
                    CurrentContentRelativePath(),
                    request.Content,
                    fileName,
                    request.ContentType);
            return MarkdownEditorAssetUploadResult.Success($"![{uploaded.FileName}]({uploaded.RelativePath})");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return MarkdownEditorAssetUploadResult.Failure(Text("contentEditor.asset.pasteFailed"));
        }
    }

    /// <summary>重写编辑器内预览 HTML 的本地 assets 链接，让未发布内容和 draft 图片也能立即显示。</summary>
    private string RewriteEditorAssetReferences(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || !HasEditorDocument)
        {
            return html;
        }

        return EditorAssetAttributeRegex().Replace(html, match =>
        {
            var target = WebUtility.HtmlDecode(match.Groups["target"].Value);
            var previewUrl = BuildEditorAssetPreviewUrl(target);
            if (previewUrl is null)
            {
                return match.Value;
            }

            var attribute = match.Groups["attribute"].Value;
            var quote = match.Groups["quote"].Value;
            return $"{attribute}={quote}{WebUtility.HtmlEncode(previewUrl)}{quote}";
        });
    }

    private string? BuildEditorAssetPreviewUrl(string assetRelativePath)
    {
        if (IsUnsavedDraft && _draftSession is not null)
        {
            return "/Admin/Content/Assets?draft=" +
                Uri.EscapeDataString(_draftSession.DraftId) +
                "&asset=" +
                Uri.EscapeDataString(assetRelativePath);
        }

        var contentPath = CurrentContentRelativePathOrNull();
        return string.IsNullOrWhiteSpace(contentPath)
            ? null
            : "/Admin/Content/Assets?path=" +
                Uri.EscapeDataString(contentPath) +
                "&asset=" +
                Uri.EscapeDataString(assetRelativePath);
    }

    private string CurrentContentRelativePath()
        => CurrentContentRelativePathOrNull() ?? throw new InvalidOperationException("当前内容尚未落盘，不能写入 workspace assets。");

    private string? CurrentContentRelativePathOrNull()
        => _file?.RelativePath ?? Path;

    private static bool LooksLikeImageUpload(MarkdownEditorPastedImageRequest request)
    {
        if (request.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var extension = System.IO.Path.GetExtension(request.FileName ?? string.Empty).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif" or ".svg";
    }

    private static string ResolvePastedImageFileName(MarkdownEditorPastedImageRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FileName) &&
            !string.IsNullOrWhiteSpace(System.IO.Path.GetExtension(request.FileName)))
        {
            return request.FileName.Trim();
        }

        var extension = request.ContentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/avif" => ".avif",
            "image/svg+xml" => ".svg",
            _ => ".png",
        };
        return "pasted-image" + extension;
    }

    [GeneratedRegex("""(?<attribute>\b(?:src|href))=(?<quote>["'])(?<target>(?:\./)?assets/[^"'<>]+)\k<quote>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EditorAssetAttributeRegex();
}
