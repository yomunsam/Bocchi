using System.Globalization;

using Bocchi.HomeServer.Data;
using Bocchi.Workspace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using YamlDotNet.RepresentationModel;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 站点基础设置服务。workspace 的 <c>site.yaml</c> 是站点内容设置的权威来源；
/// 数据库保留一份投影供后台查询。启动时从 workspace 灌库，禁止用默认值盖掉已有文件。
/// </summary>
public sealed class SiteProfileSettingsService
{
    private readonly BocchiDbContext _db;
    private readonly BocchiDataLayout _layout;
    private readonly TimeProvider _time;
    private readonly ILogger<SiteProfileSettingsService> _logger;

    /// <summary>构造站点基础设置服务。</summary>
    public SiteProfileSettingsService(
        BocchiDbContext db,
        BocchiDataLayout layout,
        TimeProvider time,
        ILogger<SiteProfileSettingsService> logger)
    {
        _db = db;
        _layout = layout;
        _time = time;
        _logger = logger;
    }

    /// <summary>读取单站点基础设置；缺失时创建默认值。</summary>
    public async Task<SiteProfileSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _db.SiteProfileSettings
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (settings is not null)
        {
            return settings;
        }

        settings = CreateDefaultSettings();
        _db.SiteProfileSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return settings;
    }

    /// <summary>
    /// 启动时同步：若 workspace 已有 <c>site.yaml</c>，以文件为准灌入数据库；
    /// 仅当文件不存在时才把数据库投影写出。绝不在启动时用 DB 默认值覆盖已有文件。
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        var file = _layout.Workspace.SiteSettingsFile;
        if (File.Exists(file) && new FileInfo(file).Length > 0)
        {
            await ImportWorkspaceIntoDbAsync(file, cancellationToken).ConfigureAwait(false);
            return;
        }

        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        await WriteWorkspaceSiteYamlAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>保存站点基础约定：先写 workspace，再更新数据库投影。</summary>
    public async Task SaveAsync(SiteProfileSettingsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        Apply(settings, update, now.Year.ToString(CultureInfo.InvariantCulture));
        settings.UpdatedAt = now;
        await WriteWorkspaceSiteYamlAsync(settings, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>从已有 site.yaml 读取站点资料字段并写入 DB；与 DB 冲突时保留文件并记 Warning。</summary>
    private async Task ImportWorkspaceIntoDbAsync(string file, CancellationToken cancellationToken)
    {
        var root = await LoadSiteYamlRootAsync(file, cancellationToken).ConfigureAwait(false);
        var fromFile = ReadProfileFields(root);
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        if (ProfileDiffers(settings, fromFile))
        {
            _logger.LogWarning(
                "site.yaml 与数据库站点资料不一致，以 workspace 为准（title={Title}, baseUrl={BaseUrl}, language={Language}）。",
                fromFile.SiteName,
                fromFile.PublicBaseUrl,
                fromFile.Language);
        }

        settings.SiteName = fromFile.SiteName;
        settings.DefaultTitle = fromFile.DefaultTitle;
        settings.Description = fromFile.Description;
        settings.PublicBaseUrl = fromFile.PublicBaseUrl;
        settings.CopyrightNotice = fromFile.CopyrightNotice;
        settings.Language = fromFile.Language;
        settings.TimeZone = fromFile.TimeZone;
        settings.DefaultThemeId = fromFile.DefaultThemeId;
        settings.UpdatedAt = _time.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteWorkspaceSiteYamlAsync(SiteProfileSettings settings, CancellationToken cancellationToken)
    {
        var file = _layout.Workspace.SiteSettingsFile;
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var root = await LoadSiteYamlRootAsync(file, cancellationToken).ConfigureAwait(false);
        SetScalar(root, "formatVersion", WorkspaceFormatVersion.Current.ToString(CultureInfo.InvariantCulture));
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

    private static ProfileFields ReadProfileFields(YamlMappingNode root)
    {
        var year = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        var siteName = NormalizeRequired(GetScalar(root, "title"), "Bocchi");
        return new ProfileFields(
            SiteName: siteName,
            DefaultTitle: NormalizeRequired(GetScalar(root, "defaultTitle"), siteName),
            Description: NormalizeOptional(GetScalar(root, "description")),
            PublicBaseUrl: NormalizePublicBaseUrl(GetScalar(root, "baseUrl")),
            CopyrightNotice: NormalizeRequired(GetScalar(root, "copyright"), $"Copyright © {year} {siteName}."),
            Language: NormalizeRequired(GetScalar(root, "language"), "zh-CN"),
            TimeZone: NormalizeRequired(GetScalar(root, "timeZone"), "Asia/Shanghai"),
            DefaultThemeId: NormalizeRequired(GetScalar(root, "defaultThemeId"), "bocchi-mono"));
    }

    private static bool ProfileDiffers(SiteProfileSettings db, ProfileFields file)
        => !string.Equals(db.SiteName, file.SiteName, StringComparison.Ordinal)
           || !string.Equals(db.DefaultTitle, file.DefaultTitle, StringComparison.Ordinal)
           || !string.Equals(db.Description, file.Description, StringComparison.Ordinal)
           || !string.Equals(db.PublicBaseUrl, file.PublicBaseUrl, StringComparison.Ordinal)
           || !string.Equals(db.CopyrightNotice, file.CopyrightNotice, StringComparison.Ordinal)
           || !string.Equals(db.Language, file.Language, StringComparison.Ordinal)
           || !string.Equals(db.TimeZone, file.TimeZone, StringComparison.Ordinal)
           || !string.Equals(db.DefaultThemeId, file.DefaultThemeId, StringComparison.Ordinal);

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

    private static string? GetScalar(YamlMappingNode root, string key)
    {
        var nodeKey = new YamlScalarNode(key);
        if (!root.Children.TryGetValue(nodeKey, out var node) || node is not YamlScalarNode scalar)
        {
            return null;
        }

        return scalar.Value;
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

    private sealed record ProfileFields(
        string SiteName,
        string DefaultTitle,
        string Description,
        string PublicBaseUrl,
        string CopyrightNotice,
        string Language,
        string TimeZone,
        string DefaultThemeId);
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
