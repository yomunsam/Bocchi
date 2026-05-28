using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Bocchi.ContentModel;
using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.ThemeInputs.Models;
using Bocchi.Generator.Utilities;
using Bocchi.GeneratorContract;

namespace Bocchi.Generator.ThemeInputs;

/// <summary>
/// 把 <see cref="ContentGraph.ContentGraph"/> 序列化为一组 <see cref="BuildArtifact"/>（<see cref="ArtifactKind.ThemeInput"/>）。
/// 详见 <c>Docs/Milestones/M3/M3.md §3.5</c>。
/// </summary>
public sealed class ThemeInputWriter
{
    private const string StageName = "WriteThemeInputStage";
    private const string DefaultLanguageCode = "zh-CN";
    private const string PrimaryUnprefixedUrlPolicy = "PrimaryUnprefixed";

    /// <summary>共享的 <see cref="JsonSerializerOptions"/>：camelCase、缩进、UTF-8 无 BOM。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>M6 Theme Context 首批可识别语言；未知自定义语言会按 code 自描述传给 Theme。</summary>
    private static readonly Dictionary<string, ThemeContextLanguageRecord> KnownLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en-US"] = new() { Code = "en-US", NativeName = "English", EnglishName = "English" },
        ["zh-CN"] = new() { Code = "zh-CN", NativeName = "简体中文", EnglishName = "Simplified Chinese" },
        ["zh-TW"] = new() { Code = "zh-TW", NativeName = "繁體中文", EnglishName = "Traditional Chinese" },
        ["ja-JP"] = new() { Code = "ja-JP", NativeName = "日本語", EnglishName = "Japanese" },
    };

    /// <summary>Generator 用于解析 Menu label 的 Common i18n 默认值；完整前台文案仍由 Theme 自己决定。</summary>
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> CommonDisplayDefaults = new(StringComparer.Ordinal)
    {
        ["menu.home"] = CreateLanguageValues("Index", "首页", "首頁", "ホーム"),
        ["menu.posts"] = CreateLanguageValues("Writing", "写作", "寫作", "文章"),
        ["menu.works"] = CreateLanguageValues("Work", "作品", "作品", "制作"),
        ["menu.notes"] = CreateLanguageValues("Notes", "札记", "札記", "ノート"),
        ["menu.friends"] = CreateLanguageValues("Friends", "友链", "友站", "リンク"),
        ["menu.about"] = CreateLanguageValues("About", "关于", "關於", "紹介"),
        ["page.normal.name"] = CreateLanguageValues("Normal", "普通页面", "普通頁面", "通常ページ"),
    };

    private readonly TimeProvider _time;

    public ThemeInputWriter(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>创建首批 Common display ref 的默认多语言文案。</summary>
    private static Dictionary<string, string> CreateLanguageValues(
        string enUs,
        string zhCn,
        string zhTw,
        string jaJp)
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-US"] = enUs,
            ["zh-CN"] = zhCn,
            ["zh-TW"] = zhTw,
            ["ja-JP"] = jaJp,
        };

    /// <summary>
    /// 序列化所有 Theme 输入 artifact，但不实际写入 Sink；返回 artifact + 字节内容（供 caller 写到任何 Sink）。
    /// </summary>
    public IReadOnlyList<(BuildArtifact Artifact, ReadOnlyMemory<byte> Bytes)> Build(
        ContentGraph.ContentGraph graph,
        string themeId,
        BuildMode mode,
        string environment,
        bool includeDrafts,
        string? bocchiVersion,
        ThemeManifest? manifest = null,
        JsonObject? themeConfig = null,
        BuildLocalizationOptions? localization = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var generatedAt = _time.GetUtcNow();
        var result = new List<(BuildArtifact, ReadOnlyMemory<byte>)>();

        void Add(string fileName, string schemaId, object data)
        {
            var envelope = new InputEnvelope<object>
            {
                Schema = schemaId,
                ContractVersion = ThemeContractVersion.Current,
                GeneratedAt = generatedAt,
                Data = data,
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            var sha = Sha256Hex.FromBytes(bytes);
            var artifact = new BuildArtifact
            {
                Path = "/" + fileName,
                Kind = ArtifactKind.ThemeInput,
                ContentType = "application/json; charset=utf-8",
                SizeBytes = bytes.Length,
                Sha256 = sha,
                ProducedBy = StageName,
                Bytes = bytes,
            };
            result.Add((artifact, bytes));
        }

        Add("site.json", ContractSchemaIds.Site, MapSite(graph.Site));
        Add("navigation.json", ContractSchemaIds.Navigation, MapNavigation(graph, manifest, localization));
        Add("post-categories.json", ContractSchemaIds.PostCategories, graph.PostCategories.Select(MapPostCategory).ToArray());
        Add("posts.json", ContractSchemaIds.Posts, graph.Posts.Select(post => MapPost(post, graph.Site.NormalizedBaseUrl)).ToArray());
        Add("pages.json", ContractSchemaIds.Pages, graph.Pages.Select(page => MapPage(page, graph.Site.NormalizedBaseUrl)).ToArray());
        Add("works.json", ContractSchemaIds.Works, graph.Works.Select(work => MapWork(work, graph.Site.NormalizedBaseUrl)).ToArray());
        Add("notes.json", ContractSchemaIds.Notes, graph.Notes.Select(MapNote).ToArray());
        Add("friends.json", ContractSchemaIds.Friends, graph.Friends.Select(MapFriend).ToArray());
        Add("photos.json", ContractSchemaIds.Photos, Array.Empty<object>());
        Add(
            "theme-context.json",
            ContractSchemaIds.ThemeContext,
            MapThemeContext(graph, themeId, mode, environment, includeDrafts, bocchiVersion, generatedAt, manifest, themeConfig, localization));

        var buildContext = new BuildContext
        {
            BuildTime = generatedAt,
            BaseUrl = graph.Site.NormalizedBaseUrl,
            ThemeId = themeId,
            Environment = environment,
            Features = new ThemeFeatureFlags
            {
                Search = graph.Site.Settings.EnableSearch,
            },
            BocchiVersion = bocchiVersion,
            IncludeDrafts = includeDrafts,
        };
        Add("build-context.json", ContractSchemaIds.BuildContext, buildContext);

        return result;
    }

    private static ThemeContextInput MapThemeContext(
        ContentGraph.ContentGraph graph,
        string themeId,
        BuildMode mode,
        string environment,
        bool includeDrafts,
        string? bocchiVersion,
        DateTimeOffset generatedAt,
        ThemeManifest? manifest,
        JsonObject? themeConfig,
        BuildLocalizationOptions? localization)
    {
        var settings = graph.Site.Settings;
        var authorName = settings.Author?.Name ?? "Anonymous";
        var themeFeatures = manifest?.Features;
        return new ThemeContextInput
        {
            Bocchi = new ThemeContextBocchi
            {
                Version = bocchiVersion,
            },
            Build = new ThemeContextBuild
            {
                Mode = mode == BuildMode.Live ? "live" : "full",
                GeneratedAt = generatedAt,
                Environment = environment,
                IncludeDrafts = includeDrafts,
            },
            Site = new ThemeContextSite
            {
                Title = settings.Title,
                DefaultTitle = settings.DefaultTitle,
                Description = settings.Description,
                Language = settings.Language,
                TimeZone = settings.TimeZone,
                BaseUrl = graph.Site.NormalizedBaseUrl.AbsoluteUri,
                CopyrightNotice = settings.CopyrightNotice,
            },
            Localization = MapLocalization(settings, localization),
            Author = new ThemeContextAuthor
            {
                Name = authorName,
                DisplayName = authorName,
                TimeZone = settings.TimeZone,
                Email = settings.Author?.Email,
                Bio = settings.Author?.Bio,
                Links = settings.Social,
            },
            Features = new ThemeContextFeatures
            {
                Rss = settings.EnableRss,
                Sitemap = settings.EnableSitemap,
                Search = themeFeatures?.Search ?? settings.EnableSearch,
            },
            Theme = new ThemeContextTheme
            {
                Id = themeId,
                Name = manifest?.Name ?? themeId,
                Version = manifest?.Version ?? "0.0.0",
                Config = themeConfig ?? new JsonObject(),
                I18n = MapThemeI18n(manifest?.I18n),
                PageTemplates = MapThemePageTemplates(manifest),
                SpecialPages = MapThemeSpecialPages(manifest),
            },
        };
    }

    /// <summary>生成 Theme Contract 的 localization 节点；HomeServer 未注入快照时回退到 site.yaml 的单语言事实。</summary>
    private static ThemeContextLocalization MapLocalization(SiteSettings settings, BuildLocalizationOptions? options)
    {
        var primary = options is null
            ? ResolveLanguageRecord(settings.Language)
            : MapLanguageRecord(options.PrimaryLanguage, options.EnabledLanguages.FirstOrDefault(x => SameCode(x.Code, options.PrimaryLanguage)));
        var enabled = options is null
            ? [primary]
            : NormalizeEnabledLanguages(options, primary);
        return new ThemeContextLocalization
        {
            PrimaryLanguage = primary.Code,
            EnabledLanguages = enabled,
            UrlPolicy = string.IsNullOrWhiteSpace(options?.UrlPolicy) ? PrimaryUnprefixedUrlPolicy : options.UrlPolicy,
            Text = MapLocalizationText(options),
        };
    }

    /// <summary>把构建快照中的 enabled languages 清理成 Theme Context 需要的稳定列表，并强制包含主要语言。</summary>
    private static List<ThemeContextLanguageRecord> NormalizeEnabledLanguages(
        BuildLocalizationOptions options,
        ThemeContextLanguageRecord primary)
    {
        var result = new List<ThemeContextLanguageRecord>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in options.EnabledLanguages)
        {
            if (string.IsNullOrWhiteSpace(language.Code) || !seen.Add(language.Code.Trim()))
            {
                continue;
            }

            result.Add(MapLanguageRecord(language.Code, language));
        }

        if (!seen.Contains(primary.Code))
        {
            result.Insert(0, primary);
        }

        return result;
    }

    /// <summary>把 Common 与当前 Theme 私有覆盖合并成 Theme Context 的 plain text JSON object。</summary>
    private static JsonObject MapLocalizationText(BuildLocalizationOptions? options)
    {
        var result = new JsonObject();
        if (options is null)
        {
            return result;
        }

        ApplyLocalizationText(result, options.Text);
        // Theme 私有覆盖优先级高于 Common 覆盖；同 key + language 时后写覆盖前写。
        ApplyLocalizationText(result, options.ThemeTextOverrides);
        return result;
    }

    /// <summary>把一组 i18n 覆盖写入目标 JSON object；调用方负责传入优先级顺序。</summary>
    private static void ApplyLocalizationText(
        JsonObject target,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> text)
    {
        foreach (var (key, values) in text.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var normalizedKey = key.Trim();
            var languageValues = target[normalizedKey] as JsonObject ?? new JsonObject();
            foreach (var (language, value) in values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                languageValues[language.Trim()] = value.Trim();
            }

            if (languageValues.Count > 0)
            {
                target[normalizedKey] = languageValues;
            }
        }
    }

    /// <summary>把 Theme manifest 中的私有 i18n key 声明复制到 Theme Context，供 Theme 与 Dashboard 后续 UI 共用。</summary>
    private static ThemeContextThemeI18n MapThemeI18n(ThemeI18nManifest? manifest)
    {
        if (manifest is null)
        {
            return new ThemeContextThemeI18n();
        }

        return new ThemeContextThemeI18n
        {
            SupportedLanguages = manifest.SupportedLanguages
                .Where(language => !string.IsNullOrWhiteSpace(language))
                .Select(language => language.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DefaultLanguage = string.IsNullOrWhiteSpace(manifest.DefaultLanguage)
                ? null
                : manifest.DefaultLanguage.Trim(),
            Keys = manifest.Keys
                .Where(key => !string.IsNullOrWhiteSpace(key.Key) && !string.IsNullOrWhiteSpace(key.Title))
                .GroupBy(key => key.Key.Trim(), StringComparer.Ordinal)
                .Select(group => MapThemeI18nKey(group.First()))
                .OrderBy(key => key.Key, StringComparer.Ordinal)
            .ToArray(),
        };
    }

    private static ThemeContextPageTemplate[] MapThemePageTemplates(ThemeManifest? manifest)
        => NormalizePageTemplates(manifest)
            .Select(template => new ThemeContextPageTemplate
            {
                Name = template.Name,
                DisplayName = template.DisplayName,
            })
            .ToArray();

    private static ThemeContextSpecialPage[] MapThemeSpecialPages(ThemeManifest? manifest)
        => NormalizeSpecialPages(manifest)
            .Select(page => new ThemeContextSpecialPage
            {
                Name = page.Name,
                DisplayName = page.DisplayName,
                Route = NormalizeRoute(page.Route),
            })
            .ToArray();

    private static List<ThemePageTemplateManifest> NormalizePageTemplates(ThemeManifest? manifest)
    {
        var result = new List<ThemePageTemplateManifest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in manifest?.PageTemplates ?? [])
        {
            if (string.IsNullOrWhiteSpace(template.Name) ||
                string.IsNullOrWhiteSpace(template.DisplayName) ||
                !seen.Add(template.Name.Trim()))
            {
                continue;
            }

            result.Add(new ThemePageTemplateManifest
            {
                Name = template.Name.Trim(),
                DisplayName = template.DisplayName.Trim(),
            });
        }

        if (!seen.Contains("normal"))
        {
            result.Insert(0, new ThemePageTemplateManifest
            {
                Name = "normal",
                DisplayName = "i18n://common@page.normal.name",
            });
        }

        return result;
    }

    private static List<ThemeSpecialPageManifest> NormalizeSpecialPages(ThemeManifest? manifest)
    {
        var result = new List<ThemeSpecialPageManifest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in manifest?.SpecialPages ?? [])
        {
            if (string.IsNullOrWhiteSpace(page.Name) ||
                string.IsNullOrWhiteSpace(page.DisplayName) ||
                !IsValidThemeSpecialPageRoute(page.Route) ||
                !seen.Add(page.Name.Trim()))
            {
                continue;
            }

            result.Add(new ThemeSpecialPageManifest
            {
                Name = page.Name.Trim(),
                DisplayName = page.DisplayName.Trim(),
                Route = NormalizeRoute(page.Route),
            });
        }

        return result;
    }

    /// <summary>清理单个 Theme 私有 key 声明；默认值保持 plain text，不做 HTML/Markdown 解释。</summary>
    private static ThemeContextThemeI18nKey MapThemeI18nKey(ThemeI18nKeyManifest key)
        => new()
        {
            Key = key.Key.Trim(),
            Title = key.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(key.Description) ? null : key.Description.Trim(),
            DefaultValues = MapThemeI18nDefaultValues(key.DefaultValues),
        };

    /// <summary>把 Theme manifest 的默认文案值转成稳定 JSON object。</summary>
    private static JsonObject MapThemeI18nDefaultValues(IReadOnlyDictionary<string, string> defaultValues)
    {
        var result = new JsonObject();
        foreach (var (language, value) in defaultValues.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[language.Trim()] = value.Trim();
        }

        return result;
    }

    /// <summary>把构建快照语言转成 Theme Context record；缺失名称时用内置表或 code 自描述兜底。</summary>
    private static ThemeContextLanguageRecord MapLanguageRecord(string? languageCode, BuildLanguageRecord? language)
    {
        var fallback = ResolveLanguageRecord(languageCode);
        return new ThemeContextLanguageRecord
        {
            Code = string.IsNullOrWhiteSpace(language?.Code) ? fallback.Code : language.Code.Trim(),
            NativeName = string.IsNullOrWhiteSpace(language?.NativeName) ? fallback.NativeName : language.NativeName.Trim(),
            EnglishName = string.IsNullOrWhiteSpace(language?.EnglishName) ? fallback.EnglishName : language.EnglishName.Trim(),
        };
    }

    /// <summary>解析语言元数据；未知自定义 code 不报错，保持 Theme 可展示的自描述记录。</summary>
    private static ThemeContextLanguageRecord ResolveLanguageRecord(string? languageCode)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? DefaultLanguageCode : languageCode.Trim();
        return KnownLanguages.TryGetValue(normalized, out var language)
            ? language
            : new ThemeContextLanguageRecord
            {
                Code = normalized,
                NativeName = normalized,
                EnglishName = normalized,
            };
    }

    /// <summary>以大小写不敏感方式比较语言代码。</summary>
    private static bool SameCode(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static NavigationInput MapNavigation(
        ContentGraph.ContentGraph graph,
        ThemeManifest? manifest,
        BuildLocalizationOptions? localization)
    {
        var siteLanguage = graph.Site.Settings.Language;
        var pages = graph.Pages
            .GroupBy(page => page.Slug, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => CreatePageNavigationTarget(group, siteLanguage),
                StringComparer.Ordinal);
        var specialPages = NormalizeSpecialPages(manifest).ToDictionary(page => page.Name, StringComparer.Ordinal);
        var postCategories = graph.PostCategories.FlattenDepthFirst().ToDictionary(category => category.Slug, StringComparer.Ordinal);
        var items = graph.Site.Settings.Navigation
            .Select(item => MapNavigationItem(item, pages, specialPages, postCategories, manifest, localization, siteLanguage))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
        return new NavigationInput { Items = items };
    }

    private static NavigationItemInput? MapNavigationItem(
        NavigationItem item,
        IReadOnlyDictionary<string, PageNavigationTarget> pages,
        IReadOnlyDictionary<string, ThemeSpecialPageManifest> specialPages,
        IReadOnlyDictionary<string, GraphPostCategory> postCategories,
        ThemeManifest? manifest,
        BuildLocalizationOptions? localization,
        string siteLanguage)
    {
        var children = item.Children
            .Select(child => MapNavigationItem(child, pages, specialPages, postCategories, manifest, localization, siteLanguage))
            .Where(child => child is not null)
            .Select(child => child!)
            .ToArray();
        var target = item.Target;
        if (target is null)
        {
            if (children.Length == 0)
            {
                return null;
            }

            var groupDisplay = ResolveDisplayText(item.Label, item.Id, manifest, localization, siteLanguage);
            return new NavigationItemInput
            {
                Id = item.Id,
                Label = groupDisplay.Text,
                LabelI18n = groupDisplay.I18n,
                Href = null,
                LanguageHrefs = null,
                Target = null,
                Children = children,
            };
        }

        var href = ResolveNavigationHref(target, pages, specialPages, postCategories);
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var fallback = ResolveNavigationFallbackLabel(target, pages, specialPages, postCategories);
        var display = ResolveDisplayText(item.Label, fallback, manifest, localization, siteLanguage);
        return new NavigationItemInput
        {
            Id = item.Id,
            Label = display.Text,
            LabelI18n = display.I18n,
            Href = href,
            LanguageHrefs = ResolveNavigationLanguageHrefs(target, pages),
            Target = new NavigationTargetInput
            {
                Type = target.Type,
                Value = target.Value,
            },
            Children = children,
        };
    }

    private static string? ResolveNavigationHref(
        NavigationTarget target,
        IReadOnlyDictionary<string, PageNavigationTarget> pages,
        IReadOnlyDictionary<string, ThemeSpecialPageManifest> specialPages,
        IReadOnlyDictionary<string, GraphPostCategory> postCategories)
    {
        var value = string.IsNullOrWhiteSpace(target.Value) ? string.Empty : target.Value.Trim();
        return target.Type.Trim() switch
        {
            "builtin" => BuiltinHref(value),
            "page" => pages.TryGetValue(value, out var page) ? page.Fallback.SiteRelativeUrl : null,
            "themePage" => specialPages.TryGetValue(value, out var page) && IsValidThemeSpecialPageRoute(page.Route)
                ? NormalizeRoute(page.Route)
                : null,
            "postCategory" => postCategories.TryGetValue(value, out var category) ? category.SiteRelativeUrl : null,
            _ => null,
        };
    }

    private static string ResolveNavigationFallbackLabel(
        NavigationTarget target,
        IReadOnlyDictionary<string, PageNavigationTarget> pages,
        IReadOnlyDictionary<string, ThemeSpecialPageManifest> specialPages,
        IReadOnlyDictionary<string, GraphPostCategory> postCategories)
    {
        var value = string.IsNullOrWhiteSpace(target.Value) ? string.Empty : target.Value.Trim();
        return target.Type.Trim() switch
        {
            "builtin" => BuiltinDisplayRef(value) ?? value,
            "page" => pages.TryGetValue(value, out var page) ? page.Fallback.Title : value,
            "themePage" => specialPages.TryGetValue(value, out var page) ? page.DisplayName : value,
            "postCategory" => postCategories.TryGetValue(value, out var category) ? category.Name : value,
            _ => value,
        };
    }

    private static string? BuiltinHref(string value) => value switch
    {
        "home" => "/",
        "posts" => "/posts/",
        "works" => "/works/",
        "notes" => "/notes/",
        "friends" => "/friends/",
        _ => null,
    };

    private static string? BuiltinDisplayRef(string value) => value switch
    {
        "home" => "i18n://common@menu.home",
        "posts" => "i18n://common@menu.posts",
        "works" => "i18n://common@menu.works",
        "notes" => "i18n://common@menu.notes",
        "friends" => "i18n://common@menu.friends",
        _ => null,
    };

    /// <summary>创建 Page menu target 的多语言 URL 事实；Theme 只消费这里给出的路径，不再从 slug 拼 URL。</summary>
    private static PageNavigationTarget CreatePageNavigationTarget(IEnumerable<GraphPage> pages, string siteLanguage)
    {
        var variants = pages
            .OrderBy(page => page.Language, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fallback = variants.FirstOrDefault(page => SameCode(page.Language, siteLanguage)) ?? variants[0];
        var languageHrefs = variants
            .GroupBy(page => page.Language, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.First().Language,
                group => group.First().SiteRelativeUrl,
                StringComparer.OrdinalIgnoreCase);
        return new PageNavigationTarget(fallback, languageHrefs);
    }

    /// <summary>只为 Page target 暴露实际存在的语言 variant URL；内置集合页保持现有固定 URL。</summary>
    private static IReadOnlyDictionary<string, string>? ResolveNavigationLanguageHrefs(
        NavigationTarget target,
        IReadOnlyDictionary<string, PageNavigationTarget> pages)
    {
        var value = string.IsNullOrWhiteSpace(target.Value) ? string.Empty : target.Value.Trim();
        return string.Equals(target.Type.Trim(), "page", StringComparison.Ordinal) &&
            pages.TryGetValue(value, out var page) &&
            page.LanguageHrefs.Count > 1
            ? page.LanguageHrefs
            : null;
    }

    private static DisplayText ResolveDisplayText(
        string? raw,
        string fallback,
        ThemeManifest? manifest,
        BuildLocalizationOptions? localization,
        string siteLanguage)
    {
        var source = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        if (TryParseDisplayRef(source, out var scope, out var key))
        {
            return new DisplayText(
                ResolveDisplayRef(scope, key, manifest, localization, siteLanguage),
                new NavigationLabelI18nRefInput
                {
                    Scope = scope,
                    Key = key,
                    Raw = source,
                });
        }

        return new DisplayText(string.IsNullOrWhiteSpace(source) ? fallback : source, null);
    }

    private static string ResolveDisplayRef(
        string scope,
        string key,
        ThemeManifest? manifest,
        BuildLocalizationOptions? localization,
        string siteLanguage)
    {
        var language = string.IsNullOrWhiteSpace(localization?.PrimaryLanguage)
            ? siteLanguage
            : localization.PrimaryLanguage;
        if (string.Equals(scope, "common", StringComparison.Ordinal))
        {
            return ResolveLanguageValue(localization?.Text, key, language)
                ?? ResolveLanguageValue(CommonDisplayDefaults, key, language)
                ?? key;
        }

        if (string.Equals(scope, "theme", StringComparison.Ordinal))
        {
            return ResolveLanguageValue(localization?.ThemeTextOverrides, key, language)
                ?? ResolveThemeDefaultValue(manifest, key, language)
                ?? key;
        }

        return key;
    }

    private static string? ResolveLanguageValue(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? values,
        string key,
        string language)
    {
        if (values is null || !values.TryGetValue(key, out var languageValues))
        {
            return null;
        }

        if (languageValues.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return languageValues.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? ResolveThemeDefaultValue(ThemeManifest? manifest, string key, string language)
    {
        var item = manifest?.I18n?.Keys.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (item is null)
        {
            return null;
        }

        if (item.DefaultValues.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(manifest?.I18n?.DefaultLanguage) &&
            item.DefaultValues.TryGetValue(manifest.I18n.DefaultLanguage, out var fallback) &&
            !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return item.DefaultValues.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static bool TryParseDisplayRef(string value, out string scope, out string key)
    {
        scope = string.Empty;
        key = string.Empty;
        const string prefix = "i18n://";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = value[prefix.Length..];
        var separator = body.IndexOf('@', StringComparison.Ordinal);
        if (separator <= 0 || separator >= body.Length - 1)
        {
            return false;
        }

        scope = body[..separator].Trim();
        key = body[(separator + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(key);
    }

    private static bool IsValidThemeSpecialPageRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return false;
        }

        var normalized = route.Trim();
        return normalized[0] == '/'
            && !normalized.StartsWith("//", StringComparison.Ordinal)
            && !normalized.Contains("..", StringComparison.Ordinal);
    }

    private static string NormalizeRoute(string route)
        => route.Trim();

    private static SiteInput MapSite(GraphSite site) => new()
    {
        Title = site.Settings.Title,
        DefaultTitle = site.Settings.DefaultTitle,
        Description = site.Settings.Description,
        Language = site.Settings.Language,
        TimeZone = site.Settings.TimeZone,
        BaseUrl = site.NormalizedBaseUrl.AbsoluteUri,
        CopyrightNotice = site.Settings.CopyrightNotice,
        Author = site.Settings.Author,
        Social = site.Settings.Social,
        EnableRss = site.Settings.EnableRss,
        EnableSitemap = site.Settings.EnableSitemap,
        EnableSearch = site.Settings.EnableSearch,
        FeedItemCount = site.Settings.FeedItemCount,
    };

    private static PostInput MapPost(GraphPost p, Uri baseUrl) => new()
    {
        Id = p.ContentId,
        Slug = p.Slug,
        Year = p.Year,
        Title = p.Title,
        Language = p.Language,
        Localization = MapContentLocalization(p.Localization, baseUrl),
        Status = StatusToString(p.Status),
        PublishedAt = p.PublishedAt,
        UpdatedAt = p.UpdatedAt,
        Category = p.Category,
        CategorySlug = p.CategorySlug,
        Tags = p.Tags,
        Summary = p.Summary,
        Cover = MapMedia(p.Cover),
        SiteRelativeUrl = p.SiteRelativeUrl,
        CanonicalUrl = AbsoluteUrl(baseUrl, p.SiteRelativeUrl),
        Url = p.SiteRelativeUrl,
        Markdown = p.BodyMarkdown,
        Html = p.BodyHtml,
        Excerpt = p.Excerpt,
        Media = p.Media.Select(MapMediaRequired).ToArray(),
    };

    private static PageInput MapPage(GraphPage p, Uri baseUrl) => new()
    {
        Id = p.ContentId,
        Slug = p.Slug,
        Title = p.Title,
        Language = p.Language,
        Localization = MapContentLocalization(p.Localization, baseUrl),
        Status = StatusToString(p.Status),
        Order = p.Order,
        ShowInNavigation = p.ShowInNavigation,
        Summary = p.Summary,
        Template = p.Template,
        SiteRelativeUrl = p.SiteRelativeUrl,
        CanonicalUrl = AbsoluteUrl(baseUrl, p.SiteRelativeUrl),
        Url = p.SiteRelativeUrl,
        Markdown = p.BodyMarkdown,
        Html = p.BodyHtml,
        Excerpt = p.Excerpt,
        Media = p.Media.Select(MapMediaRequired).ToArray(),
    };

    private static WorkInput MapWork(GraphWork w, Uri baseUrl) => new()
    {
        Id = w.ContentId,
        Slug = w.Slug,
        Year = w.Year,
        Title = w.Title,
        Language = w.Language,
        Localization = MapContentLocalization(w.Localization, baseUrl),
        Status = StatusToString(w.Status),
        Role = w.Role,
        Period = w.Period,
        Cover = MapMedia(w.Cover),
        Links = w.Links,
        Stack = w.Stack,
        Summary = w.Summary,
        Featured = w.Featured,
        SiteRelativeUrl = w.SiteRelativeUrl,
        CanonicalUrl = AbsoluteUrl(baseUrl, w.SiteRelativeUrl),
        Url = w.SiteRelativeUrl,
        Markdown = w.BodyMarkdown,
        Html = w.BodyHtml,
        Excerpt = w.Excerpt,
        Media = w.Media.Select(MapMediaRequired).ToArray(),
    };

    private static ContentLocalizationInput MapContentLocalization(GraphContentLocalization localization, Uri baseUrl) => new()
    {
        GroupId = localization.GroupId,
        IsTranslation = localization.IsTranslation,
        SourceLanguage = localization.SourceLanguage,
        SourceContentId = localization.SourceContentId,
        Alternates = localization.Alternates.Select(alternate => MapContentAlternate(alternate, baseUrl)).ToArray(),
    };

    private static ContentAlternateInput MapContentAlternate(GraphContentAlternate alternate, Uri baseUrl) => new()
    {
        ContentId = alternate.ContentId,
        Language = alternate.Language,
        Hreflang = alternate.Language,
        Title = alternate.Title,
        SiteRelativeUrl = alternate.Url,
        Url = alternate.Url,
        Href = AbsoluteUrl(baseUrl, alternate.Url),
    };

    /// <summary>生成公开 SEO URL；Theme input 直接给绝对值，避免 Theme 自己拼接 baseUrl。</summary>
    private static string AbsoluteUrl(Uri baseUrl, string siteRelativeUrl)
        => SiteUrlResolver.Absolute(baseUrl, siteRelativeUrl).AbsoluteUri;

    private static NoteInput MapNote(GraphNote n) => new()
    {
        Id = n.Id,
        Year = n.Year,
        Status = StatusToString(n.Status),
        PublishedAt = n.PublishedAt,
        Tags = n.Tags,
        Markdown = n.BodyMarkdown,
        Html = n.BodyHtml,
        Excerpt = n.Excerpt,
        Media = n.Media.Select(MapMediaRequired).ToArray(),
    };

    private static FriendInput MapFriend(GraphFriend f) => new()
    {
        Name = f.Name,
        Url = f.Url,
        Avatar = MapMedia(f.Avatar),
        Description = f.Description,
        Tags = f.Tags,
        Status = StatusToString(f.Status),
        Order = f.Order,
    };

    private static PostCategoryInput MapPostCategory(GraphPostCategory category) => new()
    {
        Name = category.Name,
        Slug = category.Slug,
        SiteRelativeUrl = category.SiteRelativeUrl,
        Url = category.SiteRelativeUrl,
        Count = category.Count,
        Children = category.Children.Select(MapPostCategory).ToArray(),
    };

    private static MediaReferenceInput? MapMedia(MediaReference? media)
        => media is null ? null : new MediaReferenceInput(media.Path, media.Alt);

    private static MediaReferenceInput MapMediaRequired(MediaReference media)
        => new(media.Path, media.Alt);

    private static string StatusToString(ContentStatus status) => status switch
    {
        ContentStatus.Draft => "draft",
        ContentStatus.Published => "published",
        ContentStatus.Archived => "archived",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>解析后的展示文案与可选 i18n 元数据。</summary>
    /// <summary>Page menu target 的代表 variant 与各语言 URL 映射。</summary>
    private sealed record PageNavigationTarget(GraphPage Fallback, IReadOnlyDictionary<string, string> LanguageHrefs);

    private sealed record DisplayText(string Text, NavigationLabelI18nRefInput? I18n);
}
