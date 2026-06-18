namespace Bocchi.HomeServer.Data;

/// <summary>
/// Home Server 明确拥有的站点基础约定。它描述后台、预览、构建和前台 Theme 都需要知道的单站点事实。
/// </summary>
public sealed class SiteProfileSettings
{
    /// <summary>固定主键，当前 Bocchi 只有一个站点。</summary>
    public int Id { get; set; } = 1;

    /// <summary>站点名称，用于后台展示、前台站点标题和 workspace 投影。</summary>
    public string SiteName { get; set; } = "Bocchi";

    /// <summary>前台页面没有更具体标题时使用的默认标题。</summary>
    public string DefaultTitle { get; set; } = "Bocchi";

    /// <summary>站点默认描述，用于后台概览、前台 meta description 和 Theme Context。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>公开发布后的前台站点根 URL；它不同于 Home Server 内部的受保护 Preview 路由。</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>前台 footer 与 Theme Context 可读取的版权文案。</summary>
    public string CopyrightNotice { get; set; } = "Copyright © 2026 Bocchi.";

    /// <summary>站点主要语言；Dashboard UI language 不写入这里。</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>作者时区和构建日期展示使用的默认时区。</summary>
    public string TimeZone { get; set; } = "Asia/Shanghai";

    /// <summary>默认前台业务 Theme id。</summary>
    public string DefaultThemeId { get; set; } = "bocchi-mono";

    /// <summary>最后更新时间，便于设置页展示和后续审计。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
