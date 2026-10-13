namespace Bocchi.Workspace.Tests;

/// <summary><see cref="WorkspaceFormatVersion.TryRead"/> 是构建加载和后台写入共用的版本判断。</summary>
public sealed class WorkspaceFormatVersionTests
{
    [Fact]
    public void TryRead_BlankYaml_UsesDefault()
    {
        var ok = WorkspaceFormatVersion.TryRead("  \n", out var version, out var code, out var message);

        ok.Should().BeTrue();
        version.Should().Be(WorkspaceFormatVersion.DefaultWhenMissing);
        code.Should().BeNull();
        message.Should().BeNull();
    }

    [Fact]
    public void TryRead_MissingField_UsesDefault()
    {
        var ok = WorkspaceFormatVersion.TryRead("title: Demo\n", out var version, out var code, out _);

        ok.Should().BeTrue();
        version.Should().Be(WorkspaceFormatVersion.DefaultWhenMissing);
        code.Should().BeNull();
    }

    [Fact]
    public void TryRead_Current_IsSupported()
    {
        var ok = WorkspaceFormatVersion.TryRead("formatVersion: 1\ntitle: Demo\n", out var version, out var code, out _);

        ok.Should().BeTrue();
        version.Should().Be(1);
        code.Should().BeNull();
    }

    [Fact]
    public void TryRead_NewerThanCurrent_IsUnsupported()
    {
        var ok = WorkspaceFormatVersion.TryRead("formatVersion: 99\ntitle: Demo\n", out var version, out var code, out var message);

        ok.Should().BeFalse();
        version.Should().Be(99);
        code.Should().Be(WorkspaceFormatVersion.UnsupportedCode);
        message.Should().Contain("99");
    }

    [Fact]
    public void TryRead_BelowOne_IsInvalid()
    {
        var ok = WorkspaceFormatVersion.TryRead("formatVersion: 0\n", out var version, out var code, out _);

        ok.Should().BeFalse();
        version.Should().Be(0);
        code.Should().Be(WorkspaceFormatVersion.InvalidCode);
    }

    [Fact]
    public void TryRead_UnparseableYaml_IsNotAVersionRefusal()
    {
        var ok = WorkspaceFormatVersion.TryRead(":\n  - [", out _, out var code, out var message);

        ok.Should().BeFalse();
        code.Should().BeNull();
        message.Should().NotBeNullOrWhiteSpace();
    }
}
