namespace Bocchi.Workspace.Tests;

public sealed class BocchiDataLayoutTests
{
    [Fact]
    public void Layout_SeparatesWorkspaceFromSystemSpace()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-test-workspace");
        var layout = new BocchiDataLayout(root);

        var expectedRoot = Path.GetFullPath(root);
        layout.DataRoot.Should().Be(expectedRoot);

        // DataRoot 里的运行数据不属于内容 workspace。
        layout.StateDirectory.Should().Be(Path.Combine(expectedRoot, "state"));
        layout.SqliteDatabasePath.Should().Be(Path.Combine(expectedRoot, "state", "bocchi.sqlite"));
        layout.LogsDirectory.Should().Be(Path.Combine(expectedRoot, "logs"));
        layout.ThemesDirectory.Should().Be(Path.Combine(expectedRoot, "themes"));
        layout.CacheDirectory.Should().Be(Path.Combine(expectedRoot, "cache"));
        layout.DerivativesDirectory.Should().Be(Path.Combine(expectedRoot, "cache", "derivatives"));
        layout.ThemeInputDirectory.Should().Be(Path.Combine(expectedRoot, "cache", "theme-input"));
        layout.ThemeConfigDirectory.Should().Be(Path.Combine(expectedRoot, "state", "theme-config"));
        layout.OutputDirectory.Should().Be(Path.Combine(expectedRoot, "output"));
        layout.PublicOutputDirectory.Should().Be(Path.Combine(expectedRoot, "output", "public"));

        // 用户内容 workspace
        layout.WorkspaceRoot.Should().Be(Path.Combine(expectedRoot, "workspace"));
        layout.Workspace.PostsDirectory.Should().Be(Path.Combine(expectedRoot, "workspace", "posts"));
        layout.Workspace.PagesDirectory.Should().Be(Path.Combine(expectedRoot, "workspace", "pages"));
        layout.Workspace.WorksDirectory.Should().Be(Path.Combine(expectedRoot, "workspace", "works"));
        layout.Workspace.NotesDirectory.Should().Be(Path.Combine(expectedRoot, "workspace", "notes"));
        layout.Workspace.FriendsFile.Should().Be(Path.Combine(expectedRoot, "workspace", "friends", "friends.yaml"));
        layout.Workspace.SiteSettingsFile.Should().Be(Path.Combine(expectedRoot, "workspace", "site", "site.yaml"));
        layout.Workspace.NavigationFile.Should().Be(Path.Combine(expectedRoot, "workspace", "site", "navigation.yaml"));
    }

    [Fact]
    public void IWorkspace_ContractIsAccessible()
    {
        typeof(IWorkspace).Should().BeAssignableTo<object>();
        typeof(IWorkspaceLoader).Should().BeAssignableTo<object>();
    }

    [Fact]
    public void WorkspaceLayout_ToRelative_NormalizesSeparators()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-toRel");
        var layout = new WorkspaceLayout(root);

        var abs = Path.Combine(root, "posts", "2025", "hello", "index.md");
        var rel = layout.ToRelative(abs);

        rel.Should().Be("posts/2025/hello/index.md");
    }
}
