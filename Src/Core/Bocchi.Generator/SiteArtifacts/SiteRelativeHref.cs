namespace Bocchi.Generator.SiteArtifacts;

/// <summary>
/// 静态搜索结果等站内链接：只允许相对路径，拒绝 http(s)/javascript/data 等绝对或危险地址。
/// </summary>
public static class SiteRelativeHref
{
    /// <summary>判断 URL 是否可作为安全的站内相对链接。</summary>
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

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.StartsWith('/'))
        {
            return !value.Contains(':', StringComparison.Ordinal);
        }

        if (value.StartsWith("./", StringComparison.Ordinal) || value.StartsWith("../", StringComparison.Ordinal))
        {
            return !value.Contains(':', StringComparison.Ordinal);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return false;
        }

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
}
