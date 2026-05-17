namespace Bocchi.HomeServer.Data;

/// <summary>
/// 前台业务 Theme 的配置投影。M4 只提供设置入口和数据边界，完整默认 Theme 视觉归 M5。
/// </summary>
public sealed class ThemeConfigurationRecord
{
    /// <summary>数据库主键。</summary>
    public int Id { get; set; }

    /// <summary>Theme Contract 中的 Theme id。</summary>
    public string ThemeId { get; set; } = string.Empty;

    /// <summary>Theme 的 JSON 配置文本。</summary>
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>Theme 私有 i18n key 覆盖，形态为 key -> language -> plain text。</summary>
    public string I18nTextOverridesJson { get; set; } = "{}";

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
