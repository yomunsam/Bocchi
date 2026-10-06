using System.Reflection;

using Bocchi.HomeServer.Components.Pages.Admin.Content;

namespace Bocchi.HomeServer.Tests;

/// <summary>ContentEditor 把结构化字段写回 frontmatter 时的回归测试。</summary>
public sealed class ContentEditorMetadataTests
{
    private const string PostPath = "posts/2026/hello/index.md";

    /// <summary>标签框没动过时，写错的标量 tags 必须原样保留，不能被改成空列表。</summary>
    [Fact]
    public void UntouchedTagsBox_KeepsScalarTagsLine()
    {
        var editor = LoadPost("title: Hello\nslug: hello\nstatus: draft\ntags: a, b");

        var yaml = BuildYaml(editor);

        yaml.Should().Contain("tags: a, b");
        yaml.Should().NotContain("tags: []");
    }

    /// <summary>用户改了标签框，就按输入写回 sequence。</summary>
    [Fact]
    public void EditedTagsBox_WritesSequence()
    {
        var editor = LoadPost("title: Hello\nslug: hello\nstatus: draft\ntags: a, b");
        SetField(editor, "_tagsText", "x, y");

        var yaml = BuildYaml(editor);

        yaml.Should().Contain("- x").And.Contain("- y");
        yaml.Should().NotContain("a, b");
    }

    private static ContentEditor LoadPost(string yaml)
    {
        var editor = new ContentEditor { Path = PostPath };
        SetField(editor, "_yaml", yaml);
        typeof(ContentEditor)
            .GetMethod("LoadMetadataFromYaml", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string), typeof(string)])!
            .Invoke(editor, [yaml, "hello"]);
        return editor;
    }

    private static string BuildYaml(ContentEditor editor)
    {
        var method = typeof(ContentEditor).GetMethod(
            "TryBuildYamlFromFields",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(bool), typeof(string).MakeByRefType(), typeof(string).MakeByRefType()])!;
        object?[] args = [false, null, null];
        var ok = (bool)method.Invoke(editor, args)!;
        ok.Should().BeTrue(args[2] as string);
        return (string)args[1]!;
    }

    private static void SetField(object target, string name, object? value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
