namespace Bocchi.HomeServer.Data;

/// <summary>
/// Home Server 首次初始化状态。当前单站点模型只需要一行记录。
/// </summary>
public sealed class SetupState
{
    /// <summary>固定主键，单站点只保留一份 Setup 状态。</summary>
    public int Id { get; set; } = 1;

    /// <summary>Setup 完成时间，使用 UTC 保存。</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>第一个 Admin 用户 ID，用于排查初始化来源。</summary>
    public string FirstAdminUserId { get; set; } = string.Empty;

    /// <summary>Setup 时确认的工作区根目录。</summary>
    public string WorkspaceRoot { get; set; } = string.Empty;

    /// <summary>Setup 写入时使用的应用 schema 版本。</summary>
    public int SchemaVersion { get; set; } = 1;
}
