using Bocchi.Workspace.Exceptions;
using LibGit2Sharp;

namespace Bocchi.Workspace.Git;

/// <summary>
/// <see cref="IContentRepository"/> 的 LibGit2Sharp 实现。作用域严格锁定到内容空间根。
/// </summary>
public sealed class LibGit2ContentRepository : IContentRepository
{
    private readonly string _root;

    public LibGit2ContentRepository(ContentSpaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _root = layout.Root;
    }

    public bool IsRepository => Repository.IsValid(_root);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!Directory.Exists(_root))
            {
                Directory.CreateDirectory(_root);
            }

            if (!Repository.IsValid(_root))
            {
                Repository.Init(_root);
            }

            return Task.CompletedTask;
        }
        catch (LibGit2SharpException ex)
        {
            throw new ContentGitException("初始化内容空间 Git 仓库失败。", ex);
        }
    }

    public Task<ContentRepositoryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Repository.IsValid(_root))
        {
            return Task.FromResult(new ContentRepositoryStatus(false, null, null, null, null, 0));
        }

        try
        {
            using var repo = new Repository(_root);
            var status = repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false });
            var dirty = status.Where(e => e.State != FileStatus.Ignored && e.State != FileStatus.Unaltered).Count();
            var head = repo.Head?.Tip;
            var branch = repo.Head?.FriendlyName;
            return Task.FromResult(new ContentRepositoryStatus(
                true,
                head?.Sha,
                head?.MessageShort,
                head?.Author.When,
                branch,
                dirty));
        }
        catch (LibGit2SharpException ex)
        {
            throw new ContentGitException("读取内容空间 Git 状态失败。", ex);
        }
    }

    public Task<string?> CommitAllAsync(string message, ContentRepositoryAuthor author, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(author);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Repository.IsValid(_root))
        {
            throw new ContentGitException($"内容空间 '{_root}' 尚未初始化为 Git 仓库。");
        }

        try
        {
            using var repo = new Repository(_root);
            Commands.Stage(repo, "*");
            var status = repo.RetrieveStatus();
            if (!status.IsDirty)
            {
                return Task.FromResult<string?>(null);
            }

            var sig = new Signature(author.Name, author.Email, DateTimeOffset.Now);
            var commit = repo.Commit(message, sig, sig);
            return Task.FromResult<string?>(commit.Sha);
        }
        catch (LibGit2SharpException ex)
        {
            throw new ContentGitException("内容空间 Git 提交失败。", ex);
        }
    }
}
