using System.Globalization;
using System.Text;

namespace Bocchi.ContentModel;

/// <summary>内容 URL path segment 的归一化工具；与 Category slug 不同，它保留 CJK 等 Unicode 文字。</summary>
public static class ContentSlug
{
    /// <summary>把标题或候选值收束为可放进单段 URL path 的 Unicode slug；无法得到有效字符时返回空字符串。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var previousWasDash = false;
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (IsSlugLetterOrNumber(category))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousWasDash = false;
                continue;
            }

            if (IsCombiningMark(category) && builder.Length > 0 && !previousWasDash)
            {
                builder.Append(ch);
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

    private static bool IsSlugLetterOrNumber(UnicodeCategory category)
        => category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.OtherNumber;

    private static bool IsCombiningMark(UnicodeCategory category)
        => category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
}
