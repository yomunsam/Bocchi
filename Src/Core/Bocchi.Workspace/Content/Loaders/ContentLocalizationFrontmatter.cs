using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

namespace Bocchi.Workspace.Content.Loaders;

/// <summary>
/// Post / Page / Work 共用的多语言 frontmatter 解析器，集中处理文件名语言、显式 language 与 translationOf。
/// </summary>
internal static class ContentLocalizationFrontmatter
{
    private const string FallbackLanguage = "zh-CN";

    /// <summary>解析一个内容 variant 的有效语言与 localization group。</summary>
    public static (string Language, ContentLocalization Localization) Read(
        YamlDotNet.RepresentationModel.YamlMappingNode mapping,
        string? fileLanguage,
        string? defaultLanguage,
        string defaultGroupId,
        ContentLocation location,
        ContentKind kind,
        List<ContentValidationError> errors)
    {
        var frontmatterLanguage = TrimToNull(YamlAccess.GetString(mapping, "language"));
        fileLanguage = TrimToNull(fileLanguage);
        defaultLanguage = TrimToNull(defaultLanguage) ?? FallbackLanguage;

        if (frontmatterLanguage is not null &&
            fileLanguage is not null &&
            !string.Equals(frontmatterLanguage, fileLanguage, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ContentValidationError(
                location.RelativePath, kind, "language",
                ContentErrorSeverity.Error, "CONTENT_LANGUAGE_FILENAME_MISMATCH",
                $"frontmatter language='{frontmatterLanguage}' 与文件名语言 '{fileLanguage}' 不一致。"));
        }

        var language = frontmatterLanguage ?? fileLanguage ?? defaultLanguage;
        var localization = YamlAccess.GetMapping(mapping, "localization");
        var groupId = TrimToNull(localization is null ? null : YamlAccess.GetString(localization, "group"))
            ?? defaultGroupId;

        ContentTranslationSource? translationOf = null;
        var translation = localization is null ? null : YamlAccess.GetMapping(localization, "translationOf");
        if (translation is not null)
        {
            var sourceLanguage = TrimToNull(YamlAccess.GetString(translation, "language"));
            if (sourceLanguage is null)
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, kind, "localization.translationOf.language",
                    ContentErrorSeverity.Error, "CONTENT_TRANSLATION_SOURCE_MISSING_LANGUAGE",
                    "Translation variant 必须声明 localization.translationOf.language。"));
            }
            else
            {
                translationOf = new ContentTranslationSource
                {
                    Language = sourceLanguage,
                    ContentId = TrimToNull(YamlAccess.GetString(translation, "contentId")),
                };
            }
        }

        return (language, new ContentLocalization { GroupId = groupId, TranslationOf = translationOf });
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
