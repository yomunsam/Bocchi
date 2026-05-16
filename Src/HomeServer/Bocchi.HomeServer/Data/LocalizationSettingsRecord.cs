namespace Bocchi.HomeServer.Data;

/// <summary>
/// 站点本地化设置的数据库投影。Dashboard UI language 不存入这里，因为它是后台偏好，不属于站点能力。
/// </summary>
public sealed class LocalizationSettingsRecord
{
    /// <summary>固定主键；当前 Home Server 仍是单站点。</summary>
    public int Id { get; set; } = 1;

    /// <summary>站点主要语言。M6 固定主语言使用无前缀 URL。</summary>
    public string PrimaryLanguage { get; set; } = "zh-CN";

    /// <summary>已启用语言的 JSON 数组，保存完整 Language record 以便未来传给 Theme Context。</summary>
    public string EnabledLanguagesJson { get; set; } = "[]";

    /// <summary>用户自定义语言的 JSON 数组。Dashboard 只提示 BCP 47，不强行拒绝未知代码。</summary>
    public string CustomLanguagesJson { get; set; } = "[]";

    /// <summary>M6 固定的 URL policy；保留字段是为了让后续 Theme Context 有稳定来源。</summary>
    public string UrlPolicy { get; set; } = "PrimaryUnprefixed";

    /// <summary>最后更新时间，供设置页反馈与后续审计使用。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
