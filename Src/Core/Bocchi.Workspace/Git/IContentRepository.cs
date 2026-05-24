namespace Bocchi.Workspace.Git;

/// <summary>提交作者信息。</summary>
/// <param name="Name">作者名。</param>
/// <param name="Email">作者邮箱。</param>
public sealed record ContentRepositoryAuthor(string Name, string Email);

/// <summary>内容 workspace Git 仓库的当前状态。</summary>
/// <param name="IsRepository">是否已初始化为 Git 仓库。</param>
/// <param name="HeadCommitSha">HEAD 提交 SHA（截断），未初始化或无提交时为 <c>null</c>。</param>
/// <param name="HeadCommitSummary">HEAD 提交首行摘要。</param>
/// <param name="HeadCommitTime">HEAD 提交时间。</param>
/// <param name="CurrentBranch">当前分支名（未初始化时为 <c>null</c>）。</param>
/// <param name="DirtyFileCount">未提交（含未跟踪）的文件数。</param>
public sealed record ContentRepositoryStatus(
    bool IsRepository,
    string? HeadCommitSha,
    string? HeadCommitSummary,
    DateTimeOffset? HeadCommitTime,
    string? CurrentBranch,
    int DirtyFileCount);

/// <summary>内容 workspace remote 配置。</summary>
/// <param name="RemoteName">Git remote 名称，例如 origin。</param>
/// <param name="RemoteUrl">远端仓库 URL；不得包含 token 或密码。</param>
/// <param name="Branch">内容同步分支。</param>
public sealed record ContentRemoteSettings(string RemoteName, string RemoteUrl, string Branch);

/// <summary>内容 workspace remote 凭据；只在 Git 操作期间短暂存在。</summary>
/// <param name="Username">HTTPS 用户名或 SSH 用户名。</param>
/// <param name="Secret">HTTPS token/password 或 SSH key passphrase。</param>
/// <param name="SshPrivateKeyPath">预留的 SSH private key 路径；当前实现只使用 HTTPS username/password 回调。</param>
public sealed record ContentRemoteCredential(string? Username, string? Secret, string? SshPrivateKeyPath = null);

/// <summary>内容 workspace remote 操作结果。</summary>
/// <param name="Status">结果状态。</param>
/// <param name="Message">给 UI 展示的脱敏摘要。</param>
/// <param name="CommitSha">操作后的 HEAD commit SHA。</param>
public sealed record ContentRemoteOperationResult(string Status, string Message, string? CommitSha);

/// <summary>
/// 内容 workspace Git 仓库的对外契约。远程能力只作用于 workspace 根，并保持 fast-forward / no-force 约束。
/// </summary>
public interface IContentRepository
{
    /// <summary>当前内容 workspace 是否为 Git 仓库。</summary>
    bool IsRepository { get; }

    /// <summary>把当前内容 workspace 初始化为 Git 仓库（不做首次提交）。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>读取当前状态。</summary>
    Task<ContentRepositoryStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>提交所有未跟踪 / 修改的文件。返回 <c>null</c> 表示无变更，无需提交。</summary>
    Task<string?> CommitAllAsync(string message, ContentRepositoryAuthor author, CancellationToken cancellationToken = default);

    /// <summary>配置 Git remote，不写入任何凭据。</summary>
    Task ConfigureRemoteAsync(ContentRemoteSettings settings, CancellationToken cancellationToken = default);

    /// <summary>把当前本地分支推送到远端分支；不使用 force。</summary>
    Task<ContentRemoteOperationResult> PushAsync(ContentRemoteSettings settings, ContentRemoteCredential? credential, CancellationToken cancellationToken = default);

    /// <summary>从远端分支拉取，只允许 fast-forward；dirty 或分叉历史会失败。</summary>
    Task<ContentRemoteOperationResult> PullFastForwardAsync(ContentRemoteSettings settings, ContentRemoteCredential? credential, CancellationToken cancellationToken = default);

    /// <summary>把远端仓库 clone 到空 workspace；导入前的备份由上层服务负责。</summary>
    Task<ContentRemoteOperationResult> CloneIntoEmptyWorkspaceAsync(ContentRemoteSettings settings, ContentRemoteCredential? credential, CancellationToken cancellationToken = default);
}
