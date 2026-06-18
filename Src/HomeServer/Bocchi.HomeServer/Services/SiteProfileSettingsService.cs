using System.Globalization;

using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;

using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// Home Server 站点基础约定服务。数据库是运行时权威，workspace 的 <c>site.yaml</c> 是可携带投影。
/// </summary>
public sealed class SiteProfileSettingsService
{
    private readonly BocchiDbContext _db;
    private readonly BocchiDataLayout _layout;
    private readonly TimeProvider _time;

    /// <summary>构造站点基础设置服务。</summary>
    public SiteProfileSettingsService(
        BocchiDbContext db,
        BocchiDataLayout layout,
        TimeProvider time)
    {
        _db = db;
        _layout = layout;
        _time = time;
    }

    /// <summary>读取单站点基础设置；缺失时创建默认值。</summary>
    public async Task<SiteProfileSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.SiteProfileSettings.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (settings is not null)
        {
            return settings;
        }

        settings = CreateDefaultSettings();
        _db.SiteProfileSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return settings;
    }

    /// <summary>确保数据库默认记录存在，并把当前记录同步到 workspace 投影。</summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        await SyncWorkspaceSiteYamlAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存站点基础约定，并同步写入 workspace <c>site.yaml</c>。</summary>
    public async Task SaveAsync(SiteProfileSettingsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        Apply(settings, update, now.Year.ToString(CultureInfo.InvariantCulture));
        settings.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await SyncWorkspaceSiteYamlAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private SiteProfileSettings CreateDefaultSettings()
    {
        var year = _time.GetUtcNow().Year.ToString(CultureInfo.InvariantCulture);
        return new SiteProfileSettings
        {
            Id = 1,
            CopyrightNotice = $"Copyright © {year} Bocchi.",
            UpdatedAt = _time.GetUtcNow(),
        };
    }

    private static void Apply(SiteProfileSettings settings, SiteProfileSettingsUpdate update, string currentYear)
    {
        settings.SiteName = NormalizeRequired(update.SiteName, "Bocchi");
        settings.DefaultTitle = NormalizeRequired(update.DefaultTitle, settings.SiteName);
        settings.Description = NormalizeOptional(update.Description);
        settings.PublicBaseUrl = NormalizePublicBaseUrl(update.PublicBaseUrl);
        settings.CopyrightNotice = NormalizeRequired(update.CopyrightNotice, $"Copyright © {currentYear} {settings.SiteName}.");
        settings.Language = NormalizeRequired(update.Language, "zh-CN");
        settings.TimeZone = NormalizeRequired(update.TimeZone, "Asia/Shanghai");
        settings.DefaultThemeId = NormalizeRequired(update.DefaultThemeId, "bocchi-mono");
    }

    private async Task SyncWorkspaceSiteYamlAsync(SiteProfileSettings settings, CancellationToken cancellationToken)
    {
        var file = _layout.Workspace.SiteSettingsFile;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var root = await LoadSiteYamlRootAsync(file, cancellationToken).ConfigureAwait(false);
        SetScalar(root, "title", settings.SiteName);
        SetScalar(root, "defaultTitle", settings.DefaultTitle);
        SetScalar(root, "description", settings.Description);
        SetScalar(root, "language", settings.Language);
        SetScalar(root, "timeZone", settings.TimeZone);
        SetScalar(root, "baseUrl", settings.PublicBaseUrl);
        SetScalar(root, "copyright", settings.CopyrightNotice);
        SetScalar(root, "defaultThemeId", settings.DefaultThemeId);

        var stream = new YamlStream(new YamlDocument(root));
        await using var output = File.Create(file);
        await using var writer = new StreamWriter(output);
        stream.Save(writer, assignAnchors: false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<YamlMappingNode> LoadSiteYamlRootAsync(string file, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            return new YamlMappingNode();
        }

        await using var input = File.OpenRead(file);
        using var reader = new StreamReader(input);
        var raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new YamlMappingNode();
        }

        var stream = new YamlStream();
        using var stringReader = new StringReader(raw);
        stream.Load(stringReader);
        return stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root
            ? root
            : new YamlMappingNode();
    }

    private static void SetScalar(YamlMappingNode root, string key, string value)
    {
        root.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }

    private static string NormalizeRequired(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizePublicBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return string.Empty;
        }

        var absolute = uri.AbsoluteUri;
        return absolute.Length > 0 && absolute[^1] == '/' ? absolute : absolute + "/";
    }
}

/// <summary>站点基础设置保存输入。它只覆盖 Home Server 拥有的固定站点字段。</summary>
public sealed record SiteProfileSettingsUpdate
{
    /// <summary>站点名称。</summary>
    public string? SiteName { get; init; }

    /// <summary>默认前台标题。</summary>
    public string? DefaultTitle { get; init; }

    /// <summary>默认站点描述。</summary>
    public string? Description { get; init; }

    /// <summary>公开前台根 URL。</summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary>版权文案。</summary>
    public string? CopyrightNotice { get; init; }

    /// <summary>站点主要语言。</summary>
    public string? Language { get; init; }

    /// <summary>站点时区。</summary>
    public string? TimeZone { get; init; }

    /// <summary>默认前台 Theme id。</summary>
    public string? DefaultThemeId { get; init; }
}
