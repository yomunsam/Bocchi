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

    /// <summary>共享的 <see cref="JsonSerializerOptions"/>：camelCase、缩进、UTF-8 无 BOM。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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
        JsonObject? themeConfig = null)
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
            MapThemeContext(graph, themeId, environment, includeDrafts, bocchiVersion, generatedAt, manifest, themeConfig));

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
        JsonObject? themeConfig)
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
                Description = settings.Description,
                Language = settings.Language,
                TimeZone = settings.TimeZone,
                BaseUrl = graph.Site.NormalizedBaseUrl.AbsoluteUri,
            },
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
            },
        };
    }

    private static SiteInput MapSite(GraphSite site) => new()
    {
        Title = site.Settings.Title,
        Description = site.Settings.Description,
        Language = site.Settings.Language,
        TimeZone = site.Settings.TimeZone,
        BaseUrl = site.NormalizedBaseUrl.AbsoluteUri,
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
