namespace Bocchi.HomeServer.Data;

/// <summary>
/// 内容 workspace 的 Git remote 配置。它描述源内容仓库的同步目标；
/// 不参与静态站发布，也不保存任何明文凭据。
/// </summary>
public sealed class ContentWorkspaceRemoteRecord
{
    /// <summary>数据库主键。</summary>
    public int Id { get; set; }

    /// <summary>UI 中展示的 remote 名称，通常是 origin。</summary>
    public string RemoteName { get; set; } = "origin";

    /// <summary>远端仓库 URL；不得包含 token 或密码。</summary>
    public string RemoteUrl { get; set; } = string.Empty;

    /// <summary>内容同步分支，默认 main。</summary>
    public string Branch { get; set; } = "main";

    /// <summary>关联的 Git 账号连接；Generic SSH/PAT 手动模式可为空。</summary>
    public int? GitProviderConnectionId { get; set; }

    /// <summary>最近一次同步状态，例如 idle、succeeded 或 failed。</summary>
    public string LastSyncStatus { get; set; } = "idle";

    /// <summary>最近一次同步摘要；必须脱敏。</summary>
    public string? LastSyncMessage { get; set; }

    /// <summary>最近一次同步时间。</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
