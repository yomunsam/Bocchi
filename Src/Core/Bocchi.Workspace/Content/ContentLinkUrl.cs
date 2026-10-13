namespace Bocchi.Workspace.Content;

/// <summary>
/// Page / Work <c>links.url</c> 允许的地址形态。
/// 允许：http、https、mailto、站内相对路径（以 <c>/</c>、<c>./</c>、<c>../</c> 开头，或无 scheme 的路径）。
/// 拒绝：javascript、data、vbscript、file、blob、以及其它未知 scheme；也拒绝 <c>//host</c> 协议相对地址。
/// </summary>
public static class ContentLinkUrl
{
    /// <summary>判断链接 URL 是否可安全写入内容模型与静态站。</summary>
    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var value = url.Trim();
        if (value.Any(ch => char.IsControl(ch)))
        {
            return false;
        }

        // 协议相对 //evil.example — 浏览器会跟当前 scheme 走，拒绝
        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // 站根相对路径。注意：Uri.TryCreate("/about", Absolute) 在 Linux 上会变成 file:///about
        if (value.StartsWith('/'))
        {
            return !value.Contains(':', StringComparison.Ordinal)
                   && Uri.TryCreate(value, UriKind.Relative, out _);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return IsAllowedScheme(absolute.Scheme);
        }

        // 相对路径：不能看起来像带未知 scheme（冒号出现在第一个 / 之前）
        var colon = value.IndexOf(':');
        if (colon >= 0)
        {
            var slash = value.IndexOf('/');
            if (slash < 0 || colon < slash)
            {
                return false;
            }
        }

        return Uri.TryCreate(value, UriKind.Relative, out _);
    }

    private static bool IsAllowedScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
}
