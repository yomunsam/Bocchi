namespace Bocchi.Workspace.Tests;

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void Layout_SeparatesContentSpaceFromSystemSpace()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-test-workspace");
        var layout = new WorkspaceLayout(root);

        var expectedRoot = Path.GetFullPath(root);
        layout.Root.Should().Be(expectedRoot);

        // 系统空间
        layout.BocchiDirectory.Should().Be(Path.Combine(expectedRoot, ".bocchi"));
        layout.SqliteDatabasePath.Should().Be(Path.Combine(expectedRoot, ".bocchi", "bocchi.sqlite"));
        layout.LogsDirectory.Should().Be(Path.Combine(expectedRoot, ".bocchi", "logs"));
        layout.ThemesDirectory.Should().Be(Path.Combine(expectedRoot, "themes"));
        layout.ThemeInputDirectory.Should().Be(Path.Combine(expectedRoot, ".bocchi", "input"));
        layout.OutputDirectory.Should().Be(Path.Combine(expectedRoot, "output"));
        layout.PublicOutputDirectory.Should().Be(Path.Combine(expectedRoot, "output", "public"));

        // 内容空间
        layout.ContentSpaceRoot.Should().Be(Path.Combine(expectedRoot, "content"));
        layout.ContentSpace.PostsDirectory.Should().Be(Path.Combine(expectedRoot, "content", "posts"));
        layout.ContentSpace.PagesDirectory.Should().Be(Path.Combine(expectedRoot, "content", "pages"));
        layout.ContentSpace.WorksDirectory.Should().Be(Path.Combine(expectedRoot, "content", "works"));
        layout.ContentSpace.NotesDirectory.Should().Be(Path.Combine(expectedRoot, "content", "notes"));
        layout.ContentSpace.FriendsFile.Should().Be(Path.Combine(expectedRoot, "content", "friends", "friends.yaml"));
        layout.ContentSpace.SiteSettingsFile.Should().Be(Path.Combine(expectedRoot, "content", "site", "site.yaml"));
        layout.ContentSpace.NavigationFile.Should().Be(Path.Combine(expectedRoot, "content", "site", "navigation.yaml"));
    }

    [Fact]
    public void IWorkspace_ContractIsAccessible()
    {
        typeof(IWorkspace).Should().BeAssignableTo<object>();
        typeof(IWorkspaceLoader).Should().BeAssignableTo<object>();
    }

    [Fact]
    public void ContentSpaceLayout_ToRelative_NormalizesSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-toRel");
        var layout = new ContentSpaceLayout(root);

        var abs = Path.Combine(root, "posts", "2025", "hello", "index.md");
        var rel = layout.ToRelative(abs);

        rel.Should().Be("posts/2025/hello/index.md");
    }
}
