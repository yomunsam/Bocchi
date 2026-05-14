namespace Bocchi.HomeServer.Data;

/// <summary>
/// Dashboard 自身的轻量设置。它只管理后台体验，不代表前台业务 Theme。
/// </summary>
public sealed class DashboardSettings
{
    /// <summary>固定主键，单站点只保留一份 Dashboard 设置。</summary>
    public int Id { get; set; } = 1;

    /// <summary>后台显示名称，默认使用 Bocchi。</summary>
    public string SiteTitle { get; set; } = "Bocchi";

    /// <summary>后台 Overview 和设置页使用的短描述。</summary>
    public string SiteDescription { get; set; } = "Personal publishing workspace";

    /// <summary>Dashboard 明暗外观：auto、light 或 dark；不是前台 Theme Contract。</summary>
    public string AppearanceMode { get; set; } = "auto";

    /// <summary>最后更新时间，便于设置页给用户反馈。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
