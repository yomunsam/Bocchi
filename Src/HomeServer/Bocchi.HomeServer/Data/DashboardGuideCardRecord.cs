namespace Bocchi.HomeServer.Data;

/// <summary>
/// Dashboard 首页的 Guide 卡片堆栈。
/// 文案目前完全由 i18n 通过 <c>Key</c> 渲染；表里只保留排序与"是否已关闭"的状态，
/// 方便 Setup 期种入欢迎卡片、以后版本广播也可继续追加同结构记录。
/// </summary>
public sealed class DashboardGuideCardRecord
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>逻辑标识；用于映射 i18n 与去重，避免重复种入同一张卡。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>堆栈内的排序权重，数字越小越先呈现。</summary>
    public int SortOrder { get; set; }

    /// <summary>关闭时间；为 <c>null</c> 表示仍在堆栈中。</summary>
    public DateTimeOffset? DismissedAt { get; set; }

    /// <summary>创建时间，便于按"广播到达顺序"调试与排查。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
