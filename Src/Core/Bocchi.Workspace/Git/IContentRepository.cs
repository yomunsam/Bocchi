namespace Bocchi.Workspace.Git;

/// <summary>提交作者信息。</summary>
/// <param name="Name">作者名。</param>
/// <param name="Email">作者邮箱。</param>
public sealed record ContentRepositoryAuthor(string Name, string Email);

/// <summary>内容空间 Git 仓库的当前状态。</summary>
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

/// <summary>
/// 内容空间 Git 仓库的对外契约。M2 仅提供本地能力（init / status / commit）；远程能力延后到 M6。
/// </summary>
public interface IContentRepository
{
    /// <summary>当前内容空间是否为 Git 仓库。</summary>
    bool IsRepository { get; }

    /// <summary>把当前内容空间初始化为 Git 仓库（不做首次提交）。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>读取当前状态。</summary>
    Task<ContentRepositoryStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>提交所有未跟踪 / 修改的文件。返回 <c>null</c> 表示无变更，无需提交。</summary>
    Task<string?> CommitAllAsync(string message, ContentRepositoryAuthor author, CancellationToken cancellationToken = default);
}