using Bocchi.Workspace.Exceptions;

using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace Bocchi.Workspace.Git;

/// <summary>
/// <see cref="IContentRepository"/> 的 LibGit2Sharp 实现。作用域严格锁定到内容 workspace 根。
/// </summary>
public sealed class LibGit2ContentRepository : IContentRepository
{
    private readonly string _root;

    public LibGit2ContentRepository(WorkspaceLayout layout)
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
            throw new ContentGitException("初始化内容 workspace Git 仓库失败。", ex);
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
            throw new ContentGitException("读取内容 workspace Git 状态失败。", ex);
        }
    }

    public Task<string?> CommitAllAsync(string message, ContentRepositoryAuthor author, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(author);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Repository.IsValid(_root))
        {
            throw new ContentGitException($"内容 workspace '{_root}' 尚未初始化为 Git 仓库。");
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
            throw new ContentGitException("内容 workspace Git 提交失败。", ex);
        }
    }

    public Task ConfigureRemoteAsync(ContentRemoteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureSafeRemote(settings);
        try
        {
            EnsureRepository();
            using var repo = new Repository(_root);
            UpsertRemote(repo, settings);
            return Task.CompletedTask;
        }
        catch (LibGit2SharpException ex)
        {
            throw new ContentGitException("配置内容 workspace Git remote 失败。", ex);
        }
    }

    public Task<ContentRemoteOperationResult> PushAsync(
        ContentRemoteSettings settings,
        ContentRemoteCredential? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureSafeRemote(settings);
        try
        {
            EnsureRepository();
            using var repo = new Repository(_root);
            var remote = UpsertRemote(repo, settings);
            if (repo.Head.Tip is null)
            {
                throw new ContentGitException("内容 workspace 还没有提交，不能推送到远端。");
            }

            var sourceBranch = repo.Head.FriendlyName;
            var refSpec = $"refs/heads/{sourceBranch}:refs/heads/{settings.Branch.Trim()}";
            repo.Network.Push(remote, refSpec, CreatePushOptions(credential));
            return Task.FromResult(new ContentRemoteOperationResult("succeeded", "内容 workspace 已推送到远端。", repo.Head.Tip.Sha));
        }
        catch (ContentGitException)
        {
            throw;
        }
        catch (LibGit2SharpException ex)
        {
            throw new ContentGitException("推送内容 workspace 到远端失败。", ex);
        }
    }

    public Task<ContentRemoteOperationResult> PullFastForwardAsync(
        ContentRemoteSettings settings,
        ContentRemoteCredential? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureSafeRemote(settings);
        try
        {
            EnsureRepository();
            using var repo = new Repository(_root);
            EnsureClean(repo);
            var remote = UpsertRemote(repo, settings);
            Commands.Fetch(repo, remote.Name, Array.Empty<string>(), CreateFetchOptions(credential), "Fetch content workspace remote");

            var remoteBranch = repo.Branches[$"{remote.Name}/{settings.Branch.Trim()}"];
            if (remoteBranch?.Tip is null)
            {
                throw new ContentGitException("远端内容分支不存在，不能执行 fast-forward pull。");
            }

            if (repo.Head.Tip is null)
            {
                throw new ContentGitException("本地内容仓库还没有提交，请使用导入已有仓库流程。");
            }

            var localTip = repo.Head.Tip;
            var remoteTip = remoteBranch.Tip;
            if (localTip.Sha == remoteTip.Sha)
            {
                return Task.FromResult(new ContentRemoteOperationResult("succeeded", "内容 workspace 已经是最新。", localTip.Sha));
            }

            if (repo.ObjectDatabase.FindMergeBase(localTip, remoteTip) is null)
            {
                throw new ContentGitException("本地与远端内容历史无共同基线，请使用导入或手动合并。");
            }

            var divergence = repo.ObjectDatabase.CalculateHistoryDivergence(localTip, remoteTip);
            if (divergence?.AheadBy is null || divergence.BehindBy is null)
            {
                throw new ContentGitException("本地与远端内容历史无共同基线，请使用导入或手动合并。");
            }

            if (divergence.AheadBy > 0 && divergence.BehindBy > 0)
            {
                throw new ContentGitException("本地与远端内容都已有新提交，不能自动合并。");
            }

            if (divergence.AheadBy > 0)
            {
                return Task.FromResult(new ContentRemoteOperationResult("succeeded", "本地内容已经领先远端，无需拉取。", localTip.Sha));
            }

            repo.Reset(ResetMode.Hard, remoteTip);
            return Task.FromResult(new ContentRemoteOperationResult("succeeded", "内容 workspace 已 fast-forward 到远端。", remoteTip.Sha));
        }
        catch (ContentGitException)
        {
            throw;
        }
        catch (LibGit2SharpException ex)
        {
            throw new ContentGitException("从远端拉取内容 workspace 失败。", ex);
        }
    }

    public Task<ContentRemoteOperationResult> CloneIntoEmptyWorkspaceAsync(
        ContentRemoteSettings settings,
        ContentRemoteCredential? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureSafeRemote(settings);
        try
        {
            if (Directory.Exists(_root) && Directory.EnumerateFileSystemEntries(_root).Any())
            {
                throw new ContentGitException("内容 workspace 不是空目录，请先完成导入备份流程。");
            }

            Directory.CreateDirectory(_root);
            Repository.Clone(
                settings.RemoteUrl.Trim(),
                _root,
                new CloneOptions(CreateFetchOptions(credential))
                {
                    BranchName = settings.Branch.Trim(),
                });

            using var repo = new Repository(_root);
            return Task.FromResult(new ContentRemoteOperationResult("succeeded", "已从远端导入内容 workspace。", repo.Head.Tip?.Sha));
        }
        catch (ContentGitException)
        {
            throw;
        }
        catch (LibGit2SharpException ex)
        {
            throw new ContentGitException("导入远端内容 workspace 失败。", ex);
        }
    }

    private void EnsureRepository()
    {
        if (!Repository.IsValid(_root))
        {
            Repository.Init(_root);
        }
    }

    private static Remote UpsertRemote(Repository repo, ContentRemoteSettings settings)
    {
        var name = settings.RemoteName.Trim();
        var url = settings.RemoteUrl.Trim();
        var existing = repo.Network.Remotes[name];
        if (existing is not null)
        {
            repo.Network.Remotes.Remove(name);
        }

        return repo.Network.Remotes.Add(name, url);
    }

    private static void EnsureClean(Repository repo)
    {
        var status = repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false });
        if (status.IsDirty)
        {
            throw new ContentGitException("内容 workspace 有未提交修改，请先提交后再拉取远端内容。");
        }
    }

    private static void EnsureSafeRemote(ContentRemoteSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.RemoteName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.RemoteUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Branch);

        if (Uri.TryCreate(settings.RemoteUrl.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new ContentGitException("内容 remote URL 不能包含用户名、token 或密码。");
        }
    }

    private static FetchOptions CreateFetchOptions(ContentRemoteCredential? credential)
        => new() { CredentialsProvider = CreateCredentialsProvider(credential) };

    private static PushOptions CreatePushOptions(ContentRemoteCredential? credential)
        => new() { CredentialsProvider = CreateCredentialsProvider(credential) };

    private static CredentialsHandler? CreateCredentialsProvider(ContentRemoteCredential? credential)
    {
        if (credential is null)
        {
            return null;
        }

        return (_, _, _) =>
        {
            if (!string.IsNullOrWhiteSpace(credential.SshPrivateKeyPath))
            {
                // 当前 LibGit2Sharp 版本未公开 SSH key path credential 类型，避免把 key 错当成密码发送。
                throw new ContentGitException("当前 Git 依赖不支持通过 SSH private key path 认证。");
            }

            return new UsernamePasswordCredentials
            {
                Username = string.IsNullOrWhiteSpace(credential.Username) ? "oauth2" : credential.Username,
                Password = credential.Secret ?? string.Empty,
            };
        };
    }
}
