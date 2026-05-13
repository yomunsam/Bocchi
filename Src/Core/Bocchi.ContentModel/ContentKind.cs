namespace Bocchi.ContentModel;

/// <summary>
/// Bocchi MVP 内容类型枚举，与 <c>Docs/Architecture.md §4</c> 中的内容模型一一对应。
/// </summary>
public enum ContentKind
{
    Post = 0,
    Page = 1,
    Work = 2,
    Note = 3,
    FriendLink = 4,
    SiteSettings = 5,
    Photo = 6,
}
