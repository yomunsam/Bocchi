using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Bocchi.ContentModel;
using Bocchi.Generator.ContentGraph;
using Bocchi.Generator.Pipeline;
using Bocchi.Generator.ThemeInputs.Models;
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

    private readonly TimeProvider _time;

    public ThemeInputWriter(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>
    /// 序列化所有 Theme 输入 artifact，但不实际写入 Sink；返回 artifact + 字节内容（供 caller 写到任何 Sink）。
    /// </summary>
    public IReadOnlyList<(BuildArtifact Artifact, ReadOnlyMemory<byte> Bytes)> Build(
        ContentGraph.ContentGraph graph,
        string themeId,
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
            var sha = ComputeSha256(bytes);
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
        Add("navigation.json", ContractSchemaIds.Navigation, new NavigationInput { Items = graph.Site.Settings.Navigation });
        Add("posts.json", ContractSchemaIds.Posts, graph.Posts.Select(MapPost).ToArray());
        Add("pages.json", ContractSchemaIds.Pages, graph.Pages.Select(MapPage).ToArray());
        Add("works.json", ContractSchemaIds.Works, graph.Works.Select(MapWork).ToArray());
        Add("notes.json", ContractSchemaIds.Notes, graph.Notes.Select(MapNote).ToArray());
        Add("friends.json", ContractSchemaIds.Friends, graph.Friends.Select(MapFriend).ToArray());
        Add("photos.json", ContractSchemaIds.Photos, Array.Empty<object>());
        Add(
            "theme-context.json",
            ContractSchemaIds.ThemeContext,
            MapThemeContext(graph, themeId, environment, includeDrafts, bocchiVersion, generatedAt, manifest, themeConfig, localization));

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

    private static PostInput MapPost(GraphPost p) => new()
    {
        Slug = p.Slug,
        Year = p.Year,
        Title = p.Title,
        Status = StatusToString(p.Status),
        PublishedAt = p.PublishedAt,
        UpdatedAt = p.UpdatedAt,
        Category = p.Category,
        Tags = p.Tags,
        Summary = p.Summary,
        Cover = MapMedia(p.Cover),
        Url = p.SiteRelativeUrl,
        Markdown = p.BodyMarkdown,
        Html = p.BodyHtml,
        Excerpt = p.Excerpt,
        Media = p.Media.Select(MapMediaRequired).ToArray(),
    };

    private static PageInput MapPage(GraphPage p) => new()
    {
        Slug = p.Slug,
        Title = p.Title,
        Status = StatusToString(p.Status),
        Order = p.Order,
        ShowInNavigation = p.ShowInNavigation,
        Summary = p.Summary,
        Url = p.SiteRelativeUrl,
        Markdown = p.BodyMarkdown,
        Html = p.BodyHtml,
        Excerpt = p.Excerpt,
        Media = p.Media.Select(MapMediaRequired).ToArray(),
    };

    private static WorkInput MapWork(GraphWork w) => new()
    {
        Slug = w.Slug,
        Year = w.Year,
        Title = w.Title,
        Status = StatusToString(w.Status),
        Role = w.Role,
        Period = w.Period,
        Cover = MapMedia(w.Cover),
        Links = w.Links,
        Stack = w.Stack,
        Summary = w.Summary,
        Featured = w.Featured,
        Url = w.SiteRelativeUrl,
        Markdown = w.BodyMarkdown,
        Html = w.BodyHtml,
        Excerpt = w.Excerpt,
        Media = w.Media.Select(MapMediaRequired).ToArray(),
    };

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

    private static string ComputeSha256(ReadOnlyMemory<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes.Span, hash);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
