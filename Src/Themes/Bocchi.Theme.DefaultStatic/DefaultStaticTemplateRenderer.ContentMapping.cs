using System.Globalization;
using System.Text.Json;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme renderer 的内容条目、媒体、链接与多语言 variant 模型映射。</summary>
public sealed partial class DefaultStaticTemplateRenderer
{
    /// <summary>把内容输入映射成模板可访问的字典数组。</summary>
    private static Dictionary<string, object?>[] MapContentItems(IEnumerable<JsonElement> items, SiteInfo site)
        => items.Select(item => MapContentItem(item, site)).ToArray();

    /// <summary>把文章、页面、作品或短文映射成模板模型。</summary>
    private static Dictionary<string, object?> MapContentItem(JsonElement item, SiteInfo site, DefaultStaticThemeText? text = null)
    {
        var date = GetContentDate(item);
        var tags = GetStringArray(item, "tags").ToArray();
        var stack = GetStringArray(item, "stack").ToArray();
        var cover = MapMediaReference(item, "cover");
        var media = MapMediaArray(item);
        var links = MapLinkArray(item);
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["url"] = GetContentUrl(item),
            ["title"] = GetTitle(item),
            ["summary"] = GetSummary(item),
            ["html"] = GetString(item, "html"),
            ["year"] = GetString(item, "year"),
            ["status"] = GetString(item, "status"),
            ["category"] = GetString(item, "category"),
            ["categorySlug"] = GetString(item, "categorySlug"),
            ["categoryUrl"] = string.IsNullOrWhiteSpace(GetString(item, "categorySlug"))
                ? string.Empty
                : $"/posts/categories/{GetString(item, "categorySlug")}/",
            ["role"] = GetString(item, "role"),
            ["period"] = GetString(item, "period"),
            ["date"] = FormatDate(date, site.AuthorTimeZone),
            ["yearMonth"] = FormatYearMonth(date),
            ["isoDate"] = date?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ["displayDateTime"] = date?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "undated",
            ["meta"] = BuildRowMeta(item, tags),
            ["tags"] = tags,
            ["hasTags"] = tags.Length > 0,
            ["stack"] = stack,
            ["hasStack"] = stack.Length > 0,
            ["cover"] = cover,
            ["hasCover"] = cover is not null,
            ["media"] = media,
            ["hasMedia"] = media.Length > 0,
            ["links"] = links,
            ["hasLinks"] = links.Length > 0,
        };

        if (text is not null)
        {
            AddContentLocalizationModel(model, item, text);
        }

        return model;
    }

    /// <summary>为详情页补充 Theme 可直接消费的内容多语言展示模型，避免模板从 URL 或 slug 反推关系。</summary>
    private static void AddContentLocalizationModel(Dictionary<string, object?> model, JsonElement item, DefaultStaticThemeText text)
    {
        var language = GetString(item, "language", text.CurrentLanguage);
        var localization = TryGetContentLocalization(item);
        var isTranslation = localization is { } loc &&
            loc.TryGetProperty("isTranslation", out var value) &&
            value.ValueKind == JsonValueKind.True;
        var sourceLanguage = localization is { } sourceLoc ? GetString(sourceLoc, "sourceLanguage") : string.Empty;
        var sourceContentId = localization is { } sourceIdLoc ? GetString(sourceIdLoc, "sourceContentId") : string.Empty;
        var languageAlternates = MapContentLanguageAlternates(item, text, language, localization);
        var sourceAlternate = FindSourceAlternate(languageAlternates, sourceContentId, sourceLanguage);

        model["language"] = language;
        model["languageName"] = GetLanguageDisplayName(text, language);
        model["languageAlternates"] = languageAlternates;
        model["hasLanguageAlternates"] = languageAlternates.Length > 1;
        model["isTranslation"] = isTranslation;
        model["sourceLanguage"] = sourceLanguage;
        model["sourceLanguageName"] = string.IsNullOrWhiteSpace(sourceLanguage)
            ? string.Empty
            : GetLanguageDisplayName(text, sourceLanguage);
        model["sourceContentId"] = sourceContentId;
        model["sourceAlternate"] = sourceAlternate;
        model["hasSourceAlternate"] = sourceAlternate is not null;
    }

    /// <summary>读取内容输入中的 localization 节点。</summary>
    private static JsonElement? TryGetContentLocalization(JsonElement item)
        => item.TryGetProperty("localization", out var localization) && localization.ValueKind == JsonValueKind.Object
            ? localization
            : null;

    /// <summary>把 Generator 提供的 alternates 映射成语言切换控件的链接模型。</summary>
    private static Dictionary<string, object?>[] MapContentLanguageAlternates(
        JsonElement item,
        DefaultStaticThemeText text,
        string currentLanguage,
        JsonElement? localization)
    {
        if (localization is not { } loc ||
            !loc.TryGetProperty("alternates", out var alternates) ||
            alternates.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var currentContentId = GetString(item, "id");
        var result = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alternate in alternates.EnumerateArray().Where(alternate => alternate.ValueKind == JsonValueKind.Object))
        {
            var language = GetString(alternate, "language");
            var contentId = GetString(alternate, "contentId");
            var siteRelativeUrl = GetString(alternate, "siteRelativeUrl", GetString(alternate, "url"));
            var url = string.IsNullOrWhiteSpace(siteRelativeUrl) ? GetString(alternate, "href") : siteRelativeUrl;
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var stableKey = string.IsNullOrWhiteSpace(contentId) ? language : contentId;
            if (!seen.Add(stableKey))
            {
                continue;
            }

            var current = string.Equals(language, currentLanguage, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(contentId) && string.Equals(contentId, currentContentId, StringComparison.OrdinalIgnoreCase));
            result.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contentId"] = contentId,
                ["language"] = language,
                ["languageName"] = GetLanguageDisplayName(text, language),
                ["title"] = GetString(alternate, "title"),
                ["url"] = url,
                ["siteRelativeUrl"] = siteRelativeUrl,
                ["href"] = GetString(alternate, "href"),
                ["hreflang"] = GetString(alternate, "hreflang", language),
                ["current"] = current,
            });
        }

        return result.ToArray();
    }

    /// <summary>优先按来源 contentId，其次按来源 language 定位“查看原文”入口。</summary>
    private static Dictionary<string, object?>? FindSourceAlternate(
        IReadOnlyList<Dictionary<string, object?>> alternates,
        string sourceContentId,
        string sourceLanguage)
    {
        return Find(sourceContentId, "contentId") ?? Find(sourceLanguage, "language");

        Dictionary<string, object?>? Find(string value, string key)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return alternates.FirstOrDefault(alternate =>
                alternate.TryGetValue(key, out var found) &&
                string.Equals(found?.ToString(), value, StringComparison.OrdinalIgnoreCase) &&
                (!alternate.TryGetValue("current", out var current) || current is not bool isCurrent || !isCurrent));
        }
    }

    /// <summary>语言显示名优先使用 Dashboard 传入的 nativeName，其次 englishName，最后回退 code。</summary>
    private static string GetLanguageDisplayName(DefaultStaticThemeText text, string language)
    {
        var matched = text.EnabledLanguages.FirstOrDefault(
            item => string.Equals(item.Code, language, StringComparison.OrdinalIgnoreCase));
        return matched?.NativeName ?? matched?.EnglishName ?? language;
    }

    /// <summary>把作品链接数组映射成模板可访问的字典数组。</summary>
    private static Dictionary<string, object?>[] MapLinkArray(JsonElement item)
    {
        if (!item.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return links.EnumerateArray()
            .Where(link => link.ValueKind == JsonValueKind.Object)
            .Select(link => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = GetString(link, "label", GetString(link, "url")),
                ["url"] = GetString(link, "url"),
            })
            .Where(link => !string.IsNullOrWhiteSpace(link["url"]?.ToString()))
            .ToArray();
    }

    /// <summary>把友链输入映射成模板可访问的字典数组。</summary>
    private static Dictionary<string, object?>[] MapFriendItems(IEnumerable<JsonElement> friends)
        => friends.Select(MapFriendItem).ToArray();

    /// <summary>把单条友链映射成模板模型。</summary>
    private static Dictionary<string, object?> MapFriendItem(JsonElement friend)
    {
        var avatar = MapMediaReference(friend, "avatar");
        var tags = GetStringArray(friend, "tags").ToArray();
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["url"] = GetString(friend, "url"),
            ["title"] = GetTitle(friend),
            ["summary"] = GetSummary(friend),
            ["meta"] = GetSummary(friend),
            ["tags"] = tags,
            ["hasTags"] = tags.Length > 0,
            ["avatar"] = avatar,
            ["hasAvatar"] = avatar is not null,
        };
    }

    /// <summary>读取单个媒体引用对象。</summary>
    private static Dictionary<string, object?>? MapMediaReference(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var media) || media.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var path = GetString(media, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = path,
            ["alt"] = GetString(media, "alt"),
        };
    }

    /// <summary>读取内容正文引用的媒体数组。</summary>
    private static Dictionary<string, object?>[] MapMediaArray(JsonElement item)
    {
        if (!item.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return media.EnumerateArray()
            .Select(MapMediaReferenceValue)
            .Where(value => value is not null)
            .Cast<Dictionary<string, object?>>()
            .ToArray();
    }

    /// <summary>把媒体引用 JSON 对象映射成模板模型。</summary>
    private static Dictionary<string, object?>? MapMediaReferenceValue(JsonElement media)
    {
        if (media.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var path = GetString(media, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = path,
            ["alt"] = GetString(media, "alt"),
        };
    }

    /// <summary>为行式列表生成紧凑 meta 文本。</summary>
    private static string BuildRowMeta(JsonElement item, IReadOnlyList<string> tags)
    {
        var category = GetString(item, "category");
        if (!string.IsNullOrWhiteSpace(category))
        {
            return category;
        }

        var period = GetString(item, "period");
        if (!string.IsNullOrWhiteSpace(period))
        {
            return period;
        }

        return string.Join(" · ", tags.Take(2));
    }
}
