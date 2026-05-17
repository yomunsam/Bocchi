using Bocchi.Workspace.Git;

namespace Bocchi.Workspace.Tests;

public sealed class LibGit2ContentRepositoryTests
{
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
}