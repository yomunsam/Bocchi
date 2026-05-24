using Bocchi.Workspace.Exceptions;
using Bocchi.Workspace.Git;

using LibGit2Sharp;

namespace Bocchi.Workspace.Tests;

/// <summary>验证内容 workspace 的 Git 初始化、提交与 remote 安全规则。</summary>
public sealed class LibGit2ContentRepositoryTests
{
    /// <summary>初始化后能读取 dirty 状态并完成首次提交。</summary>
    [Fact]
    public async Task Initialize_StatusAndCommit_HappyPath()
    {
        using var temp = new TempDataRoot();
        var initializer = new BocchiDataInitializer(temp.Layout);
        await initializer.InitializeAsync();

        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);
        repo.IsRepository.Should().BeFalse();

        await repo.InitializeAsync();
        repo.IsRepository.Should().BeTrue();

        var status = await repo.GetStatusAsync();
        status.IsRepository.Should().BeTrue();
        status.DirtyFileCount.Should().BeGreaterThan(0); // README + .gitignore + site.yaml etc.
        status.HeadCommitSha.Should().BeNull();

        var sha = await repo.CommitAllAsync("init", new ContentRepositoryAuthor("test", "t@example.com"));
        sha.Should().NotBeNull();

        var status2 = await repo.GetStatusAsync();
        status2.HeadCommitSha.Should().NotBeNullOrEmpty();
        status2.DirtyFileCount.Should().Be(0);
    }

    /// <summary>没有变更时提交返回 null，供 UI 判断无需写入新 commit。</summary>
    [Fact]
    public async Task CommitAll_ReturnsNullWhenNothingChanged()
    {
        using var temp = new TempDataRoot();
        await new BocchiDataInitializer(temp.Layout).InitializeAsync();
        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);
        await repo.InitializeAsync();
        var author = new ContentRepositoryAuthor("test", "t@example.com");
        await repo.CommitAllAsync("init", author);

        var sha = await repo.CommitAllAsync("noop", author);
        sha.Should().BeNull();
    }

    /// <summary>remote URL 不允许包含用户名、token 或密码，避免凭据进入 git config。</summary>
    [Fact]
    public async Task ConfigureRemoteAsync_UrlWithUserInfo_Throws()
    {
        using var temp = new TempDataRoot();
        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);

        var act = async () => await repo.ConfigureRemoteAsync(new ContentRemoteSettings(
            "origin",
            "https://token:secret@example.com/owner/repo.git",
            "main"));

        await act.Should().ThrowAsync<ContentGitException>()
            .Where(ex => ex.Message.Contains("不能包含用户名", StringComparison.Ordinal));
    }

    /// <summary>空远端允许首次 push，且 remote URL 只保存纯 URL。</summary>
    [Fact]
    public async Task PushAsync_EmptyRemote_PushesCurrentBranchToConfiguredBranch()
    {
        using var temp = new TempDataRoot();
        await new BocchiDataInitializer(temp.Layout).InitializeAsync();
        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);
        await repo.InitializeAsync();
        await repo.CommitAllAsync("init", TestAuthor);

        var remotePath = Path.Combine(temp.Root, "remote.git");
        Repository.Init(remotePath, isBare: true);
        var settings = new ContentRemoteSettings("origin", remotePath, "main");

        var result = await repo.PushAsync(settings, credential: null);

        result.Status.Should().Be("succeeded");
        using var remoteRepo = new Repository(remotePath);
        remoteRepo.Branches["main"].Tip.Should().NotBeNull();
        using var localRepo = new Repository(temp.Layout.Workspace.Root);
        localRepo.Network.Remotes["origin"].Url.Should().Be(remotePath);
    }

    /// <summary>本地有未提交修改时禁止 pull，避免把用户改动和远端同步混在一起。</summary>
    [Fact]
    public async Task PullFastForwardAsync_DirtyWorkspace_Throws()
    {
        using var temp = new TempDataRoot();
        await new BocchiDataInitializer(temp.Layout).InitializeAsync();
        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);
        await repo.InitializeAsync();
        await repo.CommitAllAsync("init", TestAuthor);
        File.WriteAllText(Path.Combine(temp.Layout.Workspace.Root, "dirty.txt"), "dirty");

        var act = async () => await repo.PullFastForwardAsync(
            new ContentRemoteSettings("origin", Path.Combine(temp.Root, "remote.git"), "main"),
            credential: null);

        await act.Should().ThrowAsync<ContentGitException>()
            .Where(ex => ex.Message.Contains("未提交修改", StringComparison.Ordinal));
    }

    /// <summary>远端只领先本地时允许 fast-forward，并把 workspace 内容更新到远端 tip。</summary>
    [Fact]
    public async Task PullFastForwardAsync_RemoteAhead_FastForwards()
    {
        using var temp = new TempDataRoot();
        await new BocchiDataInitializer(temp.Layout).InitializeAsync();
        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);
        await repo.InitializeAsync();
        await repo.CommitAllAsync("local init", TestAuthor);

        var remotePath = Path.Combine(temp.Root, "remote.git");
        PushWorkspaceToBareRemote(temp.Layout.Workspace.Root, remotePath, "main");
        var remoteTip = AddRemoteCommit(remotePath, Path.Combine(temp.Root, "remote-source"), "main");

        var result = await repo.PullFastForwardAsync(
            new ContentRemoteSettings("origin", remotePath, "main"),
            credential: null);

        result.Status.Should().Be("succeeded");
        result.CommitSha.Should().Be(remoteTip);
        File.ReadAllText(Path.Combine(temp.Layout.Workspace.Root, "remote-new.txt")).Should().Be("remote");
    }

    /// <summary>本地和远端历史无共同基线时停止，不自动 merge 或 force push。</summary>
    [Fact]
    public async Task PullFastForwardAsync_UnrelatedHistory_Throws()
    {
        using var temp = new TempDataRoot();
        await new BocchiDataInitializer(temp.Layout).InitializeAsync();
        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);
        await repo.InitializeAsync();
        await repo.CommitAllAsync("local init", TestAuthor);

        var remotePath = Path.Combine(temp.Root, "remote.git");
        CreateBareRemoteWithCommit(remotePath, Path.Combine(temp.Root, "remote-source"), "main");
        var act = async () => await repo.PullFastForwardAsync(
            new ContentRemoteSettings("origin", remotePath, "main"),
            credential: null);

        await act.Should().ThrowAsync<ContentGitException>()
            .Where(ex => ex.Message.Contains("无共同基线", StringComparison.Ordinal));
    }

    /// <summary>测试提交作者。</summary>
    private static readonly ContentRepositoryAuthor TestAuthor = new("test", "t@example.com");

    /// <summary>创建一个包含独立历史的 bare remote。</summary>
    private static void CreateBareRemoteWithCommit(string remotePath, string sourcePath, string branch)
    {
        Repository.Init(remotePath, isBare: true);
        Repository.Init(sourcePath);
        using var source = new Repository(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "remote.txt"), "remote");
        Commands.Stage(source, "*");
        var signature = new Signature("remote", "remote@example.com", DateTimeOffset.UtcNow);
        source.Commit("remote init", signature, signature);
        var remote = source.Network.Remotes.Add("origin", remotePath);
        source.Network.Push(remote, $"refs/heads/{source.Head.FriendlyName}:refs/heads/{branch}");
    }

    /// <summary>把当前 workspace 历史推送到一个新的 bare remote。</summary>
    private static void PushWorkspaceToBareRemote(string workspaceRoot, string remotePath, string branch)
    {
        Repository.Init(remotePath, isBare: true);
        using var local = new Repository(workspaceRoot);
        var remote = local.Network.Remotes.Add("origin", remotePath);
        local.Network.Push(remote, $"refs/heads/{local.Head.FriendlyName}:refs/heads/{branch}");
    }

    /// <summary>在 bare remote 上追加一个提交，并返回新的远端 tip。</summary>
    private static string AddRemoteCommit(string remotePath, string sourcePath, string branch)
    {
        Repository.Clone(remotePath, sourcePath, new CloneOptions { BranchName = branch });
        using var source = new Repository(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "remote-new.txt"), "remote");
        Commands.Stage(source, "*");
        var signature = new Signature("remote", "remote@example.com", DateTimeOffset.UtcNow);
        var commit = source.Commit("remote update", signature, signature);
        source.Network.Push(source.Network.Remotes["origin"], $"refs/heads/{source.Head.FriendlyName}:refs/heads/{branch}");
        return commit.Sha;
    }
}
