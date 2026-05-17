namespace Bocchi.HomeServer.Data;

/// <summary>
/// Home Server 后台维护的分类树快照。当前只服务 Admin 编辑器，不参与前台 Menu 或 Theme Contract。
/// </summary>
public sealed class CategoryTreeRecord
{
    /// <summary>自增主键，便于后续扩展多棵分类树。</summary>
    public int Id { get; set; }

    /// <summary>分类树所属范围，例如 <c>Post</c>；同一范围只有一棵树。</summary>
    public string Scope { get; set; } = "Post";

    /// <summary>分类树 JSON；根节点数组表示 0 层并排类别。</summary>
    public string TreeJson { get; set; } = "[]";

    /// <summary>最后保存时间，用于后台状态反馈和后续审计。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
