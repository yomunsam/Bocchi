namespace Bocchi.Workspace.Tests;

public sealed class WorkspaceInitializerTests
{
    [Fact]
    public async Task Initialize_CreatesAllRequiredDirectoriesAndDefaultFiles()
    {
        using var temp = new TempWorkspace();
        var initializer = new WorkspaceInitializer(temp.Layout);

        await initializer.InitializeAsync();

        Directory.Exists(temp.Layout.BocchiDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.LogsDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.ContentSpace.PostsDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.ContentSpace.PagesDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.ContentSpace.WorksDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.ContentSpace.NotesDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.ContentSpace.SiteDirectory).Should().BeTrue();

        File.Exists(temp.Layout.ContentSpace.SiteSettingsFile).Should().BeTrue();
        File.Exists(temp.Layout.ContentSpace.NavigationFile).Should().BeTrue();
        File.Exists(temp.Layout.ContentSpace.FriendsFile).Should().BeTrue();
        File.Exists(temp.Layout.ContentSpace.ReadmeFile).Should().BeTrue();
        File.Exists(temp.Layout.ContentSpace.GitIgnoreFile).Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_IsIdempotent_AndDoesNotOverwriteUserFiles()
    {
        using var temp = new TempWorkspace();
        var initializer = new WorkspaceInitializer(temp.Layout);
        await initializer.InitializeAsync();

        var customSite = "title: 用户自定义\nbaseUrl: https://me.example/\n";
        await File.WriteAllTextAsync(temp.Layout.ContentSpace.SiteSettingsFile, customSite);

        // Run again — must not clobber.
        await initializer.InitializeAsync();
        var actual = await File.ReadAllTextAsync(temp.Layout.ContentSpace.SiteSettingsFile);
        actual.Should().Be(customSite);
    }
}