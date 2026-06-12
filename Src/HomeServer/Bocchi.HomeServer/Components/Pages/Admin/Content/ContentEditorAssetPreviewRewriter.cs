using System.Net;
using System.Text.RegularExpressions;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>重写 ContentEditor 预览 HTML 中指向当前内容组 assets/ 的本地链接。</summary>
internal static partial class ContentEditorAssetPreviewRewriter
{
    /// <summary>把草稿预览中的本地 assets 链接改写到后台资产预览端点。</summary>
    public static string RewriteForDraft(string html, string draftId)
        => Rewrite(html, "draft", draftId);

    /// <summary>把已保存内容预览中的本地 assets 链接改写到后台资产预览端点。</summary>
    public static string RewriteForContent(string html, string contentRelativePath)
        => Rewrite(html, "path", contentRelativePath);

    private static string Rewrite(string html, string scopeQueryName, string scopeQueryValue)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(scopeQueryValue))
        {
            return html;
        }

        return AssetAttributeRegex().Replace(html, match =>
        {
            var assetPath = NormalizeAssetPath(WebUtility.HtmlDecode(match.Groups["target"].Value));
            var previewUrl = "/Admin/Content/Assets?" +
                scopeQueryName +
                "=" +
                Uri.EscapeDataString(scopeQueryValue) +
                "&asset=" +
                Uri.EscapeDataString(assetPath);
            var attribute = match.Groups["attribute"].Value;
            var quote = match.Groups["quote"].Value;
            return $"{attribute}={quote}{WebUtility.HtmlEncode(previewUrl)}{quote}";
        });
    }

    private static string NormalizeAssetPath(string value)
    {
        while (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value;
    }

    [GeneratedRegex("""(?<attribute>(?<![\w:-])(?:src|href))=(?<quote>["'])(?<target>(?:\./)?assets/[^"'<>]+)\k<quote>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetAttributeRegex();
}
