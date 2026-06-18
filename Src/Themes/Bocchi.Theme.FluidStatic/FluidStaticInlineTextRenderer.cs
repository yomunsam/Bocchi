using System.Net;
using System.Text;

namespace Bocchi.Theme.FluidStatic;

/// <summary>默认 Theme 的受控 inline 文本标记渲染器，只处理 Theme schema 显式声明的表现层文本。</summary>
internal static class FluidStaticInlineTextRenderer
{
    /// <summary>纯文本格式；输入只会被 HTML encode，不解释任何标记。</summary>
    public const string PlainFormat = "plain";

    /// <summary>允许 <c>[color=...]</c> 的文本格式。</summary>
    public const string InlineColorFormat = "inlineColor";

    /// <summary>把指定格式的文本渲染为可安全注入模板的 HTML 片段。</summary>
    public static string Render(string value, string? format)
        => IsInlineColorFormat(format) ? RenderInlineColor(value) : WebUtility.HtmlEncode(value);

    /// <summary>判断格式声明是否为 inline color；大小写不敏感以兼容手写 schema。</summary>
    public static bool IsInlineColorFormat(string? format)
        => string.Equals(format?.Trim(), InlineColorFormat, StringComparison.OrdinalIgnoreCase);

    /// <summary>将 schema 文本格式归一化到公开取值；未知格式降级为 plain。</summary>
    public static string NormalizeFormat(string? format)
        => IsInlineColorFormat(format) ? InlineColorFormat : PlainFormat;

    /// <summary>渲染 <c>[color=...]</c> 标记；非法 tag 会按普通文本输出，避免把用户输入升级成 HTML。</summary>
    private static string RenderInlineColor(string value)
    {
        var builder = new StringBuilder(value.Length);
        var openSpans = 0;
        var index = 0;
        while (index < value.Length)
        {
            var open = value.IndexOf('[', index);
            if (open < 0)
            {
                AppendEncoded(builder, value[index..]);
                break;
            }

            AppendEncoded(builder, value[index..open]);
            var close = value.IndexOf(']', open + 1);
            if (close < 0)
            {
                AppendEncoded(builder, value[open..]);
                break;
            }

            var token = value[open..(close + 1)];
            if (TryReadOpeningColor(token, out var cssColor))
            {
                builder.Append("<span style=\"color:").Append(cssColor).Append("\">");
                openSpans++;
            }
            else if (IsClosingColor(token) && openSpans > 0)
            {
                builder.Append("</span>");
                openSpans--;
            }
            else
            {
                AppendEncoded(builder, token);
            }

            index = close + 1;
        }

        while (openSpans > 0)
        {
            builder.Append("</span>");
            openSpans--;
        }

        return builder.ToString();
    }

    /// <summary>追加经过 HTML encode 的普通文本。</summary>
    private static void AppendEncoded(StringBuilder builder, string value)
        => builder.Append(WebUtility.HtmlEncode(value));

    /// <summary>解析 opening color tag，且只接受白名单颜色值。</summary>
    private static bool TryReadOpeningColor(string token, out string cssColor)
    {
        cssColor = string.Empty;
        var body = token[1..^1].Trim();
        var separator = body.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }

        var tagName = body[..separator].Trim();
        var color = body[(separator + 1)..].Trim();
        return string.Equals(tagName, "color", StringComparison.OrdinalIgnoreCase)
            && TryNormalizeColor(color, out cssColor);
    }

    /// <summary>判断 token 是否为 closing color tag。</summary>
    private static bool IsClosingColor(string token)
    {
        var body = token[1..^1].Trim();
        return body.StartsWith('/')
            && string.Equals(body[1..].Trim(), "color", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把用户输入的颜色归一化到安全 CSS 值；目前只允许 hex 和 accent token。</summary>
    private static bool TryNormalizeColor(string value, out string cssColor)
    {
        cssColor = string.Empty;
        if (string.Equals(value, "accent", StringComparison.OrdinalIgnoreCase))
        {
            cssColor = "var(--accent)";
            return true;
        }

        if (IsHexColor(value))
        {
            cssColor = value;
            return true;
        }

        return false;
    }

    /// <summary>判断颜色是否为 #RGB 或 #RRGGBB。</summary>
    private static bool IsHexColor(string value)
    {
        if (value.Length is not 4 and not 7 || value[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!char.IsAsciiHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
