namespace Bocchi.HomeServer.Data;

/// <summary>
/// Dashboard 自身的轻量设置。站点名称、前台 URL 等基础约定由 <see cref="SiteProfileSettings"/> 管理。
/// </summary>
public sealed class DashboardSettings
{
    /// <summary>固定主键，单站点只保留一份 Dashboard 设置。</summary>
    public int Id { get; set; } = 1;

    /// <summary>Dashboard 明暗外观：auto、light 或 dark；不是前台 Theme Contract。</summary>
    public string AppearanceMode { get; set; } = "auto";

    /// <summary>最后更新时间，便于设置页给用户反馈。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
