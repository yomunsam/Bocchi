namespace Bocchi.Workspace.Tests;

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void Layout_BuildsSubdirectoriesUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-test-workspace");
        var layout = new WorkspaceLayout(root);

        layout.Root.Should().Be(root);
        layout.ContentDirectory.Should().Be(Path.Combine(root, "content"));
        layout.MediaDirectory.Should().Be(Path.Combine(root, "media"));
        layout.SiteDirectory.Should().Be(Path.Combine(root, "site"));
        layout.ThemesDirectory.Should().Be(Path.Combine(root, "themes"));
        layout.BocchiDirectory.Should().Be(Path.Combine(root, ".bocchi"));
        layout.ThemeInputDirectory.Should().Be(Path.Combine(root, ".bocchi", "input"));
        layout.OutputDirectory.Should().Be(Path.Combine(root, "output"));
        layout.PublicOutputDirectory.Should().Be(Path.Combine(root, "output", "public"));
        layout.SqliteDatabasePath.Should().Be(Path.Combine(root, ".bocchi", "bocchi.sqlite"));
    }

    [Fact]
    public void IWorkspace_ContractIsAccessible()
    {
        typeof(IWorkspace).Should().BeAssignableTo<object>();
        typeof(IWorkspaceLoader).Should().BeAssignableTo<object>();
    }
}
