namespace Bocchi.Workspace.Tests;

public sealed class BocchiDataInitializerTests
{
    [Fact]
    public async Task Initialize_CreatesAllRequiredDirectoriesAndDefaultFiles()
    {
        using var temp = new TempDataRoot();
        var initializer = new BocchiDataInitializer(temp.Layout);

        await initializer.InitializeAsync();

        Directory.Exists(temp.Layout.StateDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.LogsDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.Workspace.PostsDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.Workspace.PagesDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.Workspace.WorksDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.Workspace.NotesDirectory).Should().BeTrue();
        Directory.Exists(temp.Layout.Workspace.SiteDirectory).Should().BeTrue();

        File.Exists(temp.Layout.Workspace.SiteSettingsFile).Should().BeTrue();
        File.Exists(temp.Layout.Workspace.NavigationFile).Should().BeTrue();
        File.Exists(temp.Layout.Workspace.FriendsFile).Should().BeTrue();
        File.Exists(temp.Layout.Workspace.ReadmeFile).Should().BeTrue();
        File.Exists(temp.Layout.Workspace.GitIgnoreFile).Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_IsIdempotent_AndDoesNotOverwriteUserFiles()
    {
        using var temp = new TempDataRoot();
        var initializer = new BocchiDataInitializer(temp.Layout);
        await initializer.InitializeAsync();

        var customSite = "title: 用户自定义\nbaseUrl: https://me.example/\n";
        await File.WriteAllTextAsync(temp.Layout.Workspace.SiteSettingsFile, customSite);

        // Run again — must not clobber.
        await initializer.InitializeAsync();
        var actual = await File.ReadAllTextAsync(temp.Layout.Workspace.SiteSettingsFile);
        actual.Should().Be(customSite);
    }
}