namespace Bocchi.ContentModel;

/// <summary>
/// 内容条目的发布状态。
/// <para>
/// 用于 <see cref="Post"/>、<see cref="Page"/>、<see cref="Work"/>、<see cref="Note"/>、
/// <see cref="FriendLink"/> 等内容类型。具体取值集合在 M2 落地内容扫描时可能扩展，
/// 但已有取值保持向后兼容。
/// </para>
/// </summary>
public enum ContentStatus
{
    /// <summary>草稿，不进入构建输出。</summary>
    Draft = 0,

    /// <summary>已发布，参与构建与 Feed/Sitemap。</summary>
    Published = 1,

    /// <summary>归档，仍可访问但不出现在常规列表中。</summary>
    Archived = 2,
}
