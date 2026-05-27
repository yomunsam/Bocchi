using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme renderer 的页面级模型、站点模型和本地化文案模型组装。</summary>
public sealed partial class DefaultStaticTemplateRenderer
{
    /// <summary>默认 accent，配置值不合法时使用它避免 CSS 注入。</summary>
    private const string DefaultAccentColor = "#E85D3A";

    /// <summary>首页配置文案注入浏览器端 i18n JSON 时使用的虚拟 key。</summary>
    private const string HomeHeroTitleClientKey = "theme.config.home.heroTitle";

    /// <summary>首页副标题配置文案注入浏览器端 i18n JSON 时使用的虚拟 key。</summary>
    private const string HomeHeroSubtitleClientKey = "theme.config.home.heroSubtitle";

    /// <summary>首页 tag 配置文案注入浏览器端 i18n JSON 时使用的虚拟 key 前缀。</summary>
    private const string HomeTagClientKeyPrefix = "theme.config.home.tag.";

    /// <summary>浏览器端语言切换需要同步的 Theme chrome 文案 key。</summary>
    private static readonly string[] ClientI18nKeys =
    [
        "menu.home",
        "menu.posts",
        "menu.works",
        "menu.notes",
        "menu.friends",
        "theme.defaultStatic.homeSelectedWriting",
        "theme.defaultStatic.homeSelectedWork",
        "theme.defaultStatic.homeRecentNotes",
        "theme.defaultStatic.all",
        "theme.defaultStatic.postsDescription",
        "theme.defaultStatic.worksDescription",
        "theme.defaultStatic.notesDescription",
        "theme.defaultStatic.friendsDescription",
        "theme.defaultStatic.emptyList",
        "theme.defaultStatic.colophonBuiltWith",
        "theme.defaultStatic.linkLabel",
        "theme.defaultStatic.pageLabel",
        "theme.defaultStatic.articleBackPrefix",
        "theme.defaultStatic.notFoundDescription",
        "theme.defaultStatic.toggleAppearance",
        "theme.defaultStatic.openMenu",
        "theme.defaultStatic.languageLabel",
        "theme.defaultStatic.languageSwitchLabel",
        "theme.defaultStatic.currentLanguageLabel",
        "theme.defaultStatic.appearanceLabel",
        "theme.defaultStatic.appearanceAuto",
        "theme.defaultStatic.appearanceLight",
        "theme.defaultStatic.appearanceDark",
        "content.translationNotice",
        "content.viewOriginal",
        "common.previous",
        "common.next",
        "common.backHome",
    ];

    /// <summary>创建列表页通用模板模型。</summary>
    private static Dictionary<string, object?> CreateListingModel(
        SiteInfo site,
        DefaultStaticThemeText text,
        string title,
        string titleKey,
        string currentPath,
        string description,
        string descriptionKey,
        string number,
        Dictionary<string, object?>[] items,
        string emptyText)
    {
        var model = CreatePageModel(site, text, title, currentPath);
        model["hero"] = CreateHeroModel(title, description, number, titleKey, descriptionKey);
        model["items"] = items;
        model["hasItems"] = items.Length > 0;
        model["itemCount"] = items.Length;
        model["emptyText"] = emptyText;
        model["emptyTextKey"] = "theme.defaultStatic.emptyList";
        return model;
    }

    /// <summary>创建所有页面共享的模板模型。</summary>
    private static Dictionary<string, object?> CreatePageModel(
        SiteInfo site,
        DefaultStaticThemeText text,
        string title,
        string currentPath,
        JsonElement? contentItem = null)
    {
        var fullTitle = string.Equals(title, text.Get("menu.home"), StringComparison.Ordinal) ? site.DefaultTitle : $"{title} · {site.Title}";
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["site"] = CreateSiteModel(site),
            ["text"] = CreateTextModel(text),
            ["localization"] = CreateLocalizationModel(text),
            ["page"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["title"] = title,
                ["fullTitle"] = fullTitle,
                ["currentPath"] = currentPath,
                ["canonicalUrl"] = GetPageCanonicalUrl(site, currentPath, contentItem),
                ["alternates"] = GetPageAlternates(contentItem).ToArray(),
            },
            ["navigation"] = CreateNavigationModel(currentPath, site.Navigation),
        };
    }

    /// <summary>读取 Generator 给出的 canonical URL；列表页等非内容页使用站点 baseUrl 兜底。</summary>
    private static string GetPageCanonicalUrl(SiteInfo site, string currentPath, JsonElement? contentItem)
    {
        if (contentItem is { } item)
        {
            var canonicalUrl = GetString(item, "canonicalUrl");
            if (!string.IsNullOrWhiteSpace(canonicalUrl))
            {
                return canonicalUrl;
            }
        }

        return ToAbsoluteUrl(site.BaseUrl, currentPath);
    }

    /// <summary>读取 Generator 预先计算的 hreflang 关系；Theme 只负责原样输出。</summary>
    private static IEnumerable<Dictionary<string, object?>> GetPageAlternates(JsonElement? contentItem)
    {
        if (contentItem is not { } item ||
            !item.TryGetProperty("localization", out var localization) ||
            localization.ValueKind != JsonValueKind.Object ||
            !localization.TryGetProperty("alternates", out var alternates) ||
            alternates.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var alternate in alternates.EnumerateArray().Where(alternate => alternate.ValueKind == JsonValueKind.Object))
        {
            var hreflang = GetString(alternate, "hreflang", GetString(alternate, "language"));
            var href = GetString(alternate, "href");
            if (string.IsNullOrWhiteSpace(hreflang) || string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            yield return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["hreflang"] = hreflang,
                ["href"] = href,
            };
        }
    }

    /// <summary>把站点根相对路径拼成绝对 URL；baseUrl 缺失或非法时保留相对路径，避免构建失败。</summary>
    private static string ToAbsoluteUrl(string baseUrl, string siteRelativeUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return string.IsNullOrWhiteSpace(siteRelativeUrl) ? "/" : siteRelativeUrl;
        }

        var normalizedBase = baseUri.AbsoluteUri.EndsWith('/') ? baseUri : new Uri(baseUri.AbsoluteUri + "/");
        var trimmed = siteRelativeUrl.StartsWith('/') ? siteRelativeUrl[1..] : siteRelativeUrl;
        return new Uri(normalizedBase, trimmed).AbsoluteUri;
    }

    /// <summary>创建布局层使用的站点模型。</summary>
    private static Dictionary<string, object?> CreateSiteModel(SiteInfo site)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = site.Title,
            ["defaultTitle"] = site.DefaultTitle,
            ["description"] = site.DescriptionOrFallback,
            ["language"] = site.Language,
            ["baseUrl"] = site.BaseUrl,
            ["copyrightNotice"] = site.CopyrightNotice,
            ["authorName"] = site.AuthorName,
            ["authorTimeZone"] = site.AuthorTimeZone,
            ["accentColor"] = site.AccentColor,
            ["generatedYear"] = site.GeneratedYear,
        };
    }

    /// <summary>创建顶栏和移动端导航使用的链接模型，保留 Menu tree 的嵌套结构。</summary>
    private static Dictionary<string, object?>[] CreateNavigationModel(string currentPath, JsonElement[] navigation)
        => navigation.Select(item => CreateNavigationItem(item, currentPath)).ToArray();

    /// <summary>创建单个导航链接模型并递归标记当前页。</summary>
    private static Dictionary<string, object?> CreateNavigationItem(JsonElement item, string currentPath)
    {
        var href = GetString(item, "href");
        var hasHref = !string.IsNullOrWhiteSpace(href);
        var children = item.TryGetProperty("children", out var childArray) && childArray.ValueKind == JsonValueKind.Array
            ? childArray.EnumerateArray().Select(child => CreateNavigationItem(child, currentPath)).ToArray()
            : [];
        var current = (hasHref && (string.Equals(href, currentPath, StringComparison.Ordinal) ||
            href != "/" && currentPath.StartsWith(href, StringComparison.Ordinal))) ||
            children.Any(child => child.TryGetValue("current", out var childCurrent) && childCurrent is true);
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["href"] = hasHref ? href : null,
            ["hasHref"] = hasHref,
            ["i18nKey"] = ReadNavigationI18nKey(item),
            ["label"] = GetString(item, "label"),
            ["current"] = current,
            ["children"] = children,
            ["hasChildren"] = children.Length > 0,
        };
        model["childrenHtml"] = RenderNavigationChildrenHtml(children, mobile: false);
        model["mobileChildrenHtml"] = RenderNavigationChildrenHtml(children, mobile: true);
        return model;
    }

    private static string? ReadNavigationI18nKey(JsonElement item)
    {
        var key = item.TryGetProperty("labelI18n", out var i18n) && i18n.ValueKind == JsonValueKind.Object
            ? GetString(i18n, "key")
            : string.Empty;
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>把嵌套 Menu 子树预渲染为 HTML，避免模板系统依赖递归 include。</summary>
    private static string RenderNavigationChildrenHtml(Dictionary<string, object?>[] children, bool mobile)
    {
        if (children.Length == 0)
        {
            return string.Empty;
        }

        var listClass = mobile ? "mobile-nav__children" : "nav__children";
        var itemClass = mobile ? "mobile-nav__item" : "nav__item";
        var builder = new StringBuilder();
        builder.Append("<ul class=\"").Append(listClass).Append("\">");
        foreach (var child in children)
        {
            var href = child.TryGetValue("href", out var hrefValue) ? hrefValue?.ToString() ?? string.Empty : string.Empty;
            var hasHref = !string.IsNullOrWhiteSpace(href);
            var label = child.TryGetValue("label", out var labelValue) ? labelValue?.ToString() ?? string.Empty : string.Empty;
            var i18nKey = child.TryGetValue("i18nKey", out var keyValue) ? keyValue?.ToString() ?? string.Empty : string.Empty;
            var current = child.TryGetValue("current", out var currentValue) && currentValue is true;
            var nestedHtml = child.TryGetValue(mobile ? "mobileChildrenHtml" : "childrenHtml", out var htmlValue)
                ? htmlValue?.ToString() ?? string.Empty
                : string.Empty;
            builder.Append("<li class=\"").Append(itemClass).Append("\">");
            if (hasHref)
            {
                builder.Append("<a href=\"")
                    .Append(WebUtility.HtmlEncode(href))
                    .Append('"');
                if (current)
                {
                    builder.Append(" aria-current=\"page\"");
                }

                AppendI18nAttribute(builder, i18nKey);
                builder.Append('>')
                    .Append(WebUtility.HtmlEncode(label))
                    .Append("</a>");
            }
            else
            {
                builder.Append("<span class=\"").Append(mobile ? "mobile-nav__label" : "nav__label").Append('"');
                AppendI18nAttribute(builder, i18nKey);
                builder.Append('>')
                    .Append(WebUtility.HtmlEncode(label))
                    .Append("</span>");
            }

            builder
                .Append(nestedHtml)
                .Append("</li>");
        }

        builder.Append("</ul>");
        return builder.ToString();
    }

    private static void AppendI18nAttribute(StringBuilder builder, string i18nKey)
    {
        if (!string.IsNullOrWhiteSpace(i18nKey))
        {
            builder.Append(" data-bocchi-i18n=\"").Append(WebUtility.HtmlEncode(i18nKey)).Append('"');
        }
    }

    /// <summary>创建页面 Hero 模型。</summary>
    private static Dictionary<string, object?> CreateHeroModel(string title, string description, string number, string? titleKey = null, string? descriptionKey = null)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["title"] = title,
            ["titleKey"] = titleKey ?? string.Empty,
            ["description"] = description,
            ["descriptionKey"] = descriptionKey ?? string.Empty,
            ["number"] = number,
        };
    }

    /// <summary>创建首页 Theme 配置文案模型，同时准备浏览器端语言切换需要的虚拟 i18n key。</summary>
    private static HomeCopyModel CreateHomeCopyModel(
        JsonElement context,
        DefaultStaticThemeText text,
        IReadOnlyDictionary<string, string> configTextFormats)
    {
        var titleValues = ReadLocalizedTextConfig(context, ["theme", "config", "home", "heroTitle"]);
        var subtitleValues = ReadLocalizedTextConfig(context, ["theme", "config", "home", "heroSubtitle"]);
        var tagValues = ReadLocalizedTextListConfig(context, ["theme", "config", "home", "tags"]);
        var title = ResolveLocalizedText(titleValues, text);
        var subtitle = ResolveLocalizedText(subtitleValues, text);
        var titleFormat = GetTextFormat(configTextFormats, "home.heroTitle");
        var subtitleFormat = GetTextFormat(configTextFormats, "home.heroSubtitle");
        var currentTags = ResolveLocalizedList(tagValues, text);
        var slotCount = Math.Max(currentTags.Length, tagValues.Values.Select(tagList => tagList.Length).DefaultIfEmpty(0).Max());
        var tagSlots = Enumerable.Range(0, slotCount)
            .Select(index => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = index < currentTags.Length ? currentTags[index] : string.Empty,
                ["i18nKey"] = HomeTagClientKeyPrefix + index.ToString(CultureInfo.InvariantCulture),
            })
            .ToArray();

        var clientText = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [HomeHeroTitleClientKey] = titleValues,
            [HomeHeroSubtitleClientKey] = subtitleValues,
        };
        for (var index = 0; index < slotCount; index++)
        {
            clientText[HomeTagClientKeyPrefix + index.ToString(CultureInfo.InvariantCulture)] =
                tagValues.ToDictionary(
                    pair => pair.Key,
                    pair => index < pair.Value.Length ? pair.Value[index] : string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }

        var templateModel = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["heroTitle"] = title,
            ["heroTitleHtml"] = DefaultStaticInlineTextRenderer.Render(title, titleFormat),
            ["heroTitleKey"] = HomeHeroTitleClientKey,
            ["heroTitleFormat"] = ToClientTextFormat(titleFormat),
            ["heroSubtitle"] = subtitle,
            ["heroSubtitleHtml"] = DefaultStaticInlineTextRenderer.Render(subtitle, subtitleFormat),
            ["heroSubtitleKey"] = HomeHeroSubtitleClientKey,
            ["heroSubtitleFormat"] = ToClientTextFormat(subtitleFormat),
            ["tagSlots"] = tagSlots,
            ["hasTags"] = tagSlots.Length > 0,
        };
        return new HomeCopyModel(templateModel, clientText);
    }

    /// <summary>读取字段声明的文本格式；未声明时返回 plain。</summary>
    private static string GetTextFormat(IReadOnlyDictionary<string, string> formats, string key)
        => formats.TryGetValue(key, out var format) ? format : DefaultStaticInlineTextRenderer.PlainFormat;

    /// <summary>转换为前端 data attribute；plain 不输出属性，保持普通 i18n 路径。</summary>
    private static string ToClientTextFormat(string format)
        => DefaultStaticInlineTextRenderer.IsInlineColorFormat(format) ? DefaultStaticInlineTextRenderer.InlineColorFormat : string.Empty;

    /// <summary>读取 Theme 配置中的多语言文本对象；非对象值被当作当前语言的单值配置处理。</summary>
    private static Dictionary<string, string> ReadLocalizedTextConfig(JsonElement context, IReadOnlyList<string> path)
    {
        var value = TryGetPath(context, path);
        if (value is not { } element)
        {
            return [];
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            return string.IsNullOrWhiteSpace(raw) ? [] : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [""] = raw.Trim(),
            };
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return element.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            .ToDictionary(
                property => property.Name.Trim(),
                property => property.Value.GetString()!.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>读取 Theme 配置中的多语言文本列表对象；每个语言值必须是字符串数组。</summary>
    private static Dictionary<string, string[]> ReadLocalizedTextListConfig(JsonElement context, IReadOnlyList<string> path)
    {
        var value = TryGetPath(context, path);
        if (value is not { ValueKind: JsonValueKind.Object } element)
        {
            return [];
        }

        return element.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.Array)
            .Select(property => new KeyValuePair<string, string[]>(
                property.Name.Trim(),
                property.Value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    .Select(item => item.GetString()!.Trim())
                    .ToArray()))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>按当前语言、站点主要语言、任意可用值的顺序解析多语言文本。</summary>
    private static string ResolveLocalizedText(Dictionary<string, string> values, DefaultStaticThemeText text)
    {
        foreach (var language in CreateConfigLanguageFallbacks(values.Keys, text))
        {
            if (values.TryGetValue(language, out var value))
            {
                return value;
            }
        }

        return values.Values.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>按当前语言、站点主要语言、任意可用值的顺序解析多语言列表。</summary>
    private static string[] ResolveLocalizedList(Dictionary<string, string[]> values, DefaultStaticThemeText text)
    {
        foreach (var language in CreateConfigLanguageFallbacks(values.Keys, text))
        {
            if (values.TryGetValue(language, out var value))
            {
                return value;
            }
        }

        return values.Values.FirstOrDefault() ?? [];
    }

    /// <summary>为 Theme 配置多语言值创建回退顺序，额外保留空语言 key 兼容单值配置。</summary>
    private static IEnumerable<string> CreateConfigLanguageFallbacks(IEnumerable<string> availableLanguages, DefaultStaticThemeText text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in new[] { text.CurrentLanguage, text.PrimaryLanguage, string.Empty })
        {
            if (seen.Add(language))
            {
                yield return language;
            }
        }

        foreach (var language in text.EnabledLanguages.Select(language => language.Code).Concat(availableLanguages))
        {
            if (!string.IsNullOrWhiteSpace(language) && seen.Add(language))
            {
                yield return language;
            }
        }
    }

    /// <summary>创建模板中用短属性名访问的前台文案模型，避免 Fluid 解析带点号的 i18n key。</summary>
    private static Dictionary<string, object?> CreateTextModel(DefaultStaticThemeText text)
    {
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["homeSelectedWriting"] = text.Get("theme.defaultStatic.homeSelectedWriting"),
            ["homeSelectedWork"] = text.Get("theme.defaultStatic.homeSelectedWork"),
            ["homeRecentNotes"] = text.Get("theme.defaultStatic.homeRecentNotes"),
            ["homeFriends"] = text.Get("menu.friends"),
            ["all"] = text.Get("theme.defaultStatic.all"),
            ["emptyList"] = text.Get("theme.defaultStatic.emptyList"),
            ["colophonBuiltWith"] = text.Get("theme.defaultStatic.colophonBuiltWith"),
            ["linkLabel"] = text.Get("theme.defaultStatic.linkLabel"),
            ["pageLabel"] = text.Get("theme.defaultStatic.pageLabel"),
            ["articleBackPrefix"] = text.Get("theme.defaultStatic.articleBackPrefix"),
            ["toggleAppearance"] = text.Get("theme.defaultStatic.toggleAppearance"),
            ["openMenu"] = text.Get("theme.defaultStatic.openMenu"),
            ["languageLabel"] = text.Get("theme.defaultStatic.languageLabel"),
            ["languageSwitchLabel"] = text.Get("theme.defaultStatic.languageSwitchLabel"),
            ["currentLanguageLabel"] = text.Get("theme.defaultStatic.currentLanguageLabel"),
            ["appearanceLabel"] = text.Get("theme.defaultStatic.appearanceLabel"),
            ["appearanceAuto"] = text.Get("theme.defaultStatic.appearanceAuto"),
            ["appearanceLight"] = text.Get("theme.defaultStatic.appearanceLight"),
            ["appearanceDark"] = text.Get("theme.defaultStatic.appearanceDark"),
            ["translationNotice"] = text.Get("content.translationNotice"),
            ["viewOriginal"] = text.Get("content.viewOriginal"),
            ["previous"] = text.Get("common.previous"),
            ["next"] = text.Get("common.next"),
            ["backHome"] = text.Get("common.backHome"),
        };
    }

    /// <summary>创建模板可访问的站点本地化模型；默认 Theme 目前只展示当前语言，路径切换留给内容多语言页生成。</summary>
    private static Dictionary<string, object?> CreateLocalizationModel(
        DefaultStaticThemeText text,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? extraText = null)
    {
        var currentLanguage = text.EnabledLanguages.FirstOrDefault(
            language => string.Equals(language.Code, text.CurrentLanguage, StringComparison.OrdinalIgnoreCase));

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["currentLanguage"] = text.CurrentLanguage,
            ["currentLanguageName"] = currentLanguage?.NativeName ?? currentLanguage?.EnglishName ?? text.CurrentLanguage,
            ["primaryLanguage"] = text.PrimaryLanguage,
            ["textJson"] = text.BuildClientJson(ClientI18nKeys, extraText),
            ["languages"] = text.EnabledLanguages
                .Select(language => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = language.Code,
                    ["nativeName"] = language.NativeName,
                    ["englishName"] = language.EnglishName,
                })
                .ToArray(),
            ["hasMultipleLanguages"] = text.EnabledLanguages.Length > 1,
        };
    }

    /// <summary>过滤发布内容；预览构建可以通过 includeDrafts 纳入草稿。</summary>
    private static IEnumerable<JsonElement> FilterVisible(IEnumerable<JsonElement> items, bool includeDrafts)
        => items.Where(item => includeDrafts || string.Equals(GetString(item, "status"), "published", StringComparison.OrdinalIgnoreCase));

    /// <summary>限制首页展示数量。</summary>
    private static JsonElement[] Limit(JsonElement[] items, int count)
        => items.Take(Math.Max(0, count)).ToArray();

    /// <summary>获取前一条内容。</summary>
    private static JsonElement? Previous(JsonElement[] items, int index)
        => index > 0 ? items[index - 1] : null;

    /// <summary>获取后一条内容。</summary>
    private static JsonElement? Next(JsonElement[] items, int index)
        => index + 1 < items.Length ? items[index + 1] : null;

    /// <summary>读取内容标题。</summary>
    private static string GetTitle(JsonElement item)
        => GetString(item, "title", GetString(item, "name", "Untitled"));

    /// <summary>读取内容摘要。</summary>
    private static string GetSummary(JsonElement item)
        => GetString(item, "summary", GetString(item, "description", GetString(item, "excerpt")));

    /// <summary>读取排序字段。</summary>
    private static int GetOrder(JsonElement item)
        => item.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number ? order.GetInt32() : 0;

    /// <summary>读取内容发布时间或更新时间。</summary>
    private static DateTimeOffset? GetContentDate(JsonElement item)
    {
        foreach (var key in new[] { "publishedAt", "updatedAt" })
        {
            var raw = GetString(item, key);
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>读取内容的 canonical 站点根相对 URL；优先使用新的 siteRelativeUrl 字段。</summary>
    private static string GetContentUrl(JsonElement item)
    {
        var siteRelativeUrl = GetString(item, "siteRelativeUrl");
        return string.IsNullOrWhiteSpace(siteRelativeUrl) ? GetString(item, "url") : siteRelativeUrl;
    }

    /// <summary>读取字符串属性。</summary>
    private static string GetString(JsonElement item, string key, string fallback = "")
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    /// <summary>读取字符串数组属性。</summary>
    private static IEnumerable<string> GetStringArray(JsonElement item, string key)
        => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty)
            : [];

    /// <summary>读取配置中的整数。</summary>
    private static int GetConfigInt(JsonElement root, string[] path, int fallback)
    {
        var value = TryGetPath(root, path);
        return value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number) ? number : fallback;
    }

    /// <summary>按路径读取嵌套 JSON 值。</summary>
    private static JsonElement? TryGetPath(JsonElement root, IReadOnlyList<string> path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>格式化年月。</summary>
    private static string FormatYearMonth(DateTimeOffset? value)
        => value?.ToString("yyyy - MM", CultureInfo.InvariantCulture) ?? "undated";

    /// <summary>格式化详情页日期。</summary>
    private static string FormatDate(DateTimeOffset? value, string timeZone)
        => value is null ? string.Empty : $"{value.Value:yyyy-MM-dd} {timeZone}";

    /// <summary>只接受十六进制 CSS color，避免配置值逃逸到 style 上下文。</summary>
    private static string NormalizeAccentColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length is not (4 or 7))
        {
            return DefaultAccentColor;
        }

        return value.Skip(1).All(Uri.IsHexDigit) ? value : DefaultAccentColor;
    }

    /// <summary>默认 Theme 输入集合。</summary>
    private sealed record ThemeInputSet(
        JsonElement ThemeContext,
        JsonElement[] Navigation,
        JsonElement[] PostCategories,
        JsonElement[] Posts,
        JsonElement[] Pages,
        JsonElement[] Works,
        JsonElement[] Notes,
        JsonElement[] Friends)
    {
        /// <summary>当前构建是否包含草稿。</summary>
        public bool IncludeDrafts => TryGetPath(ThemeContext, ["build", "includeDrafts"])?.ValueKind == JsonValueKind.True;
    }

    /// <summary>首页配置文案的模板模型与浏览器端 i18n 扩展数据。</summary>
    private sealed record HomeCopyModel(
        Dictionary<string, object?> TemplateModel,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClientText);

    /// <summary>布局层常用站点信息。</summary>
    private sealed record SiteInfo
    {
        /// <summary>站点标题。</summary>
        public required string Title { get; init; }

        /// <summary>首页和无专用标题页面使用的默认标题。</summary>
        public required string DefaultTitle { get; init; }

        /// <summary>站点描述；缺失时会使用作者 fallback。</summary>
        public required string DescriptionOrFallback { get; init; }

        /// <summary>HTML lang 使用的语言代码。</summary>
        public required string Language { get; init; }

        /// <summary>站点基础 URL。</summary>
        public required string BaseUrl { get; init; }

        /// <summary>前台 footer 使用的版权文案。</summary>
        public required string CopyrightNotice { get; init; }

        /// <summary>作者展示名。</summary>
        public required string AuthorName { get; init; }

        /// <summary>作者时区。</summary>
        public required string AuthorTimeZone { get; init; }

        /// <summary>经过 CSS 安全归一化的 accent color。</summary>
        public required string AccentColor { get; init; }

        /// <summary>前台 primary menu tree。</summary>
        public JsonElement[] Navigation { get; init; } = [];

        /// <summary>构建时间对应年份，用于 footer。</summary>
        public required int GeneratedYear { get; init; }

        /// <summary>从 theme-context.json 的 data 节点创建站点信息。</summary>
        public static SiteInfo From(JsonElement context, string currentLanguage)
        {
            var title = TryGetPath(context, ["site", "title"])?.GetString() ?? "My Site";
            var defaultTitle = TryGetPath(context, ["site", "defaultTitle"])?.GetString();
            var description = TryGetPath(context, ["site", "description"])?.GetString();
            var baseUrl = TryGetPath(context, ["site", "baseUrl"])?.GetString() ?? "/";
            var copyright = TryGetPath(context, ["site", "copyrightNotice"])?.GetString();
            var author = TryGetPath(context, ["author", "displayName"])?.GetString()
                ?? TryGetPath(context, ["author", "name"])?.GetString()
                ?? "Anonymous";
            var timeZone = TryGetPath(context, ["author", "timeZone"])?.GetString()
                ?? TryGetPath(context, ["site", "timeZone"])?.GetString()
                ?? "UTC";
            var accent = NormalizeAccentColor(TryGetPath(context, ["theme", "config", "visual", "accentColor"])?.GetString());
            var generatedAtRaw = TryGetPath(context, ["build", "generatedAt"])?.GetString();
            var generatedYear = DateTimeOffset.TryParse(generatedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var generatedAt)
                ? generatedAt.Year
                : DateTimeOffset.UtcNow.Year;

            return new SiteInfo
            {
                Title = title,
                DefaultTitle = string.IsNullOrWhiteSpace(defaultTitle) ? title : defaultTitle!,
                DescriptionOrFallback = string.IsNullOrWhiteSpace(description) ? $"A personal site by {author}." : description!,
                Language = string.IsNullOrWhiteSpace(currentLanguage) ? "zh-CN" : currentLanguage,
                BaseUrl = baseUrl,
                CopyrightNotice = string.IsNullOrWhiteSpace(copyright) ? $"{title} · {generatedYear}" : copyright!,
                AuthorName = author,
                AuthorTimeZone = timeZone,
                AccentColor = accent,
                GeneratedYear = generatedYear,
            };
        }
    }
}
