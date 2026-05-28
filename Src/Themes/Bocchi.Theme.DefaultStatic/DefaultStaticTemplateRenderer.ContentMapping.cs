using System.Globalization;
using System.Text.Json;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme renderer 的内容条目、媒体、链接与多语言 variant 模型映射。</summary>
public sealed partial class DefaultStaticTemplateRenderer
{
    /// <summary>把内容输入映射成模板可访问的字典数组。</summary>
    private static Dictionary<string, object?>[] MapContentItems(IEnumerable<JsonElement> items, SiteInfo site)
        => items.Select(item => MapContentItem(item, site)).ToArray();

    /// <summary>把列表代表项映射成模板模型，并携带同组语言 variant，供前端按当前语言切换展示。</summary>
    private static Dictionary<string, object?>[] MapListingContentItems(
        IEnumerable<JsonElement> items,
        IEnumerable<JsonElement> allItems,
        SiteInfo site,
        string currentPath)
    {
        var all = allItems.ToArray();
        var pageDirectory = GetPageDirectory(ToOutputPath(currentPath));
        return items.Select(item =>
        {
            var model = MapContentItem(item, site);
            var variants = MapListingLanguageVariants(item, all, site, pageDirectory);
            model["languageVariantsJson"] = variants.Length > 1 ? JsonSerializer.Serialize(variants) : string.Empty;
            model["hasLanguageVariants"] = variants.Length > 1;
            return model;
        }).ToArray();
    }

    /// <summary>为首页和列表页挑选每个 localization group 的代表项，避免同一篇内容的多个语言版本被当成多篇文章展示。</summary>
    private static JsonElement[] SelectListingRepresentatives(IEnumerable<JsonElement> items, string preferredLanguage)
    {
        var selected = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var localization = TryGetContentLocalization(item);
            var groupId = localization is { } loc ? GetString(loc, "groupId") : string.Empty;
            var key = !string.IsNullOrWhiteSpace(groupId)
                ? groupId
                : GetString(item, "id", GetContentUrl(item));

            if (!selected.TryGetValue(key, out var current))
            {
                selected[key] = item;
                continue;
            }

            var candidatePreferred = !string.IsNullOrWhiteSpace(preferredLanguage) &&
                string.Equals(GetString(item, "language"), preferredLanguage, StringComparison.OrdinalIgnoreCase);
            var currentPreferred = !string.IsNullOrWhiteSpace(preferredLanguage) &&
                string.Equals(GetString(current, "language"), preferredLanguage, StringComparison.OrdinalIgnoreCase);
            if (candidatePreferred && !currentPreferred)
            {
                selected[key] = item;
                continue;
            }

            var candidateTranslation = localization is { } candidateLoc &&
                candidateLoc.TryGetProperty("isTranslation", out var candidateValue) &&
                candidateValue.ValueKind == JsonValueKind.True;
            var currentLocalization = TryGetContentLocalization(current);
            var currentTranslation = currentLocalization is { } currentLoc &&
                currentLoc.TryGetProperty("isTranslation", out var currentValue) &&
                currentValue.ValueKind == JsonValueKind.True;
            if (!candidatePreferred && !currentPreferred && currentTranslation && !candidateTranslation)
            {
                selected[key] = item;
            }
        }

        return selected.Values
            .OrderByDescending(GetContentDate)
            .ThenBy(item => GetTitle(item), StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>用 Theme input 的 id 判断同一内容 variant，id 缺失时回退到 canonical URL。</summary>
    private static bool IsSameContentItem(JsonElement left, JsonElement right)
    {
        var leftId = GetString(left, "id");
        var rightId = GetString(right, "id");
        if (!string.IsNullOrWhiteSpace(leftId) || !string.IsNullOrWhiteSpace(rightId))
        {
            return string.Equals(leftId, rightId, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(GetContentUrl(left), GetContentUrl(right), StringComparison.Ordinal);
    }

    /// <summary>把文章、页面、作品或短文映射成模板模型。</summary>
    private static Dictionary<string, object?> MapContentItem(
        JsonElement item,
        SiteInfo site,
        DefaultStaticThemeText? text = null,
        bool includeArticleTime = false)
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
            ["language"] = GetString(item, "language"),
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
            ["hasArticleTime"] = false,
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

        if (includeArticleTime && text is not null)
        {
            var time = CreateArticleTimeModel(item, site, text);
            model["time"] = time;
            model["hasArticleTime"] = time.TryGetValue("hasTime", out var hasTime) && hasTime is true;
        }

        if (text is not null)
        {
            AddContentLocalizationModel(model, item, text);
        }

        return model;
    }

    /// <summary>为首页和集合页准备同组内容的可切换事实；不根据 slug 或语言列表补不存在的 URL。</summary>
    private static Dictionary<string, object?>[] MapListingLanguageVariants(
        JsonElement item,
        JsonElement[] allItems,
        SiteInfo site,
        string pageDirectory)
    {
        var groupId = GetLocalizationGroupId(item);
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return [];
        }

        return allItems
            .Where(candidate => string.Equals(GetLocalizationGroupId(candidate), groupId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => GetString(candidate, "language"), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => group.OrderByDescending(GetContentDate).ThenBy(GetTitle, StringComparer.Ordinal).First())
            .OrderBy(candidate => GetString(candidate, "language"), StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["language"] = GetString(candidate, "language"),
                ["url"] = ToPageRelativeUrl(pageDirectory, GetContentUrl(candidate)),
                ["title"] = GetTitle(candidate),
                ["summary"] = GetSummary(candidate),
                ["meta"] = BuildRowMeta(candidate, GetStringArray(candidate, "tags").ToArray()),
                ["date"] = FormatDate(GetContentDate(candidate), site.AuthorTimeZone),
                ["yearMonth"] = FormatYearMonth(GetContentDate(candidate)),
            })
            .ToArray();
    }

    /// <summary>读取 localization group id；没有 group 的单语言内容不参与前端 variant 切换。</summary>
    private static string GetLocalizationGroupId(JsonElement item)
        => TryGetContentLocalization(item) is { } localization ? GetString(localization, "groupId") : string.Empty;

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

    /// <summary>创建文章详情页的作者时区时间模型；仅 Post 详情启用，避免 Work/Page 被误认为有创建时间语义。</summary>
    private static Dictionary<string, object?> CreateArticleTimeModel(JsonElement item, SiteInfo site, DefaultStaticThemeText text)
    {
        var written = ReadContentDateTime(item, "publishedAt");
        if (written is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["hasTime"] = false,
            };
        }

        var updated = ReadContentDateTime(item, "updatedAt");
        var canToggle = updated is { } updatedValue && !IsSameInstant(written.Value, updatedValue);
        var activeKind = canToggle && site.ShowUpdatedAt ? "updated" : "written";
        var authorZone = ResolveAuthorTimeZone(site.AuthorTimeZone);
        var authorTimeZone = string.Equals(authorZone.Id, "UTC", StringComparison.Ordinal) &&
            !string.Equals(site.AuthorTimeZone, "UTC", StringComparison.OrdinalIgnoreCase)
                ? "UTC"
                : site.AuthorTimeZone;
        var writtenModel = CreateArticleTimeEntry("written", written.Value, authorTimeZone, authorZone, site.TimeZoneDisplayStyle, text);
        var updatedModel = updated is { } value
            ? CreateArticleTimeEntry("updated", value, authorTimeZone, authorZone, site.TimeZoneDisplayStyle, text)
            : null;
        var activeModel = string.Equals(activeKind, "updated", StringComparison.Ordinal) && updatedModel is not null
            ? updatedModel
            : writtenModel;
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["activeKind"] = activeModel["kind"],
            ["canToggle"] = canToggle,
            ["authorTimeZone"] = authorTimeZone,
            ["written"] = writtenModel,
            ["updated"] = updatedModel,
        };

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["hasTime"] = true,
            ["canToggle"] = canToggle,
            ["activeKind"] = activeModel["kind"],
            ["authorTimeZone"] = authorTimeZone,
            ["written"] = writtenModel,
            ["updated"] = updatedModel,
            ["active"] = activeModel,
            ["payloadJson"] = JsonSerializer.Serialize(payload),
        };
    }

    /// <summary>创建单个时间点的模板模型，显示值始终换算到作者时区。</summary>
    private static Dictionary<string, object?> CreateArticleTimeEntry(
        string kind,
        DateTimeOffset value,
        string authorTimeZone,
        TimeZoneInfo authorZone,
        string timeZoneDisplayStyle,
        DefaultStaticThemeText text)
    {
        var local = TimeZoneInfo.ConvertTime(value, authorZone);
        var offsetLabel = FormatUtcOffset(authorZone.GetUtcOffset(value.UtcDateTime));
        var labelKey = string.Equals(kind, "updated", StringComparison.Ordinal)
            ? "content.time.updatedAt"
            : "content.time.writtenAt";
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = kind,
            ["labelKey"] = labelKey,
            ["label"] = text.Get(labelKey),
            ["iso"] = value.ToString("O", CultureInfo.InvariantCulture),
            ["display"] = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["authorTimeZone"] = authorTimeZone,
            ["offsetLabel"] = offsetLabel,
            ["timeZoneLabel"] = string.Equals(timeZoneDisplayStyle, TimeZoneDisplayStyleIana, StringComparison.Ordinal)
                ? authorTimeZone
                : offsetLabel,
        };
    }

    /// <summary>读取 Theme input 中的时间字段；无效或缺失时保持为空。</summary>
    private static DateTimeOffset? ReadContentDateTime(JsonElement item, string key)
    {
        var raw = GetString(item, key);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }

    /// <summary>比较两个时间是否指向同一个 UTC instant，避免仅 offset 不同就暴露“修改时间”。</summary>
    private static bool IsSameInstant(DateTimeOffset left, DateTimeOffset right)
        => left.ToUniversalTime().Ticks == right.ToUniversalTime().Ticks;

    /// <summary>解析作者时区；配置异常时回退 UTC，避免 Theme 渲染中断。</summary>
    private static TimeZoneInfo ResolveAuthorTimeZone(string timeZone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>把 UTC offset 格式化为紧凑 badge 文本。</summary>
    private static string FormatUtcOffset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            return "UTC";
        }

        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var duration = offset.Duration();
        var hours = (int)duration.TotalHours;
        return duration.Minutes == 0
            ? $"UTC{sign}{hours.ToString(CultureInfo.InvariantCulture)}"
            : $"UTC{sign}{hours.ToString(CultureInfo.InvariantCulture)}:{duration.Minutes.ToString("00", CultureInfo.InvariantCulture)}";
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
