using System.Globalization;
using System.Text;

namespace Bocchi.ContentModel;

/// <summary>Category URL slug 归一化工具，保证后台保存与 Generator 输出使用同一套稳定规则。</summary>
public static class CategorySlug
{
    /// <summary>把输入值收束为小写 ASCII slug；无法得到有效字符时返回空字符串，由调用方决定 fallback。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasDash = false;
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousWasDash = false;
                continue;
            }

            if (!previousWasDash)
            {
                builder.Append('-');
                previousWasDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
