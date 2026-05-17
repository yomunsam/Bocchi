using Bocchi.ContentModel;
using Bocchi.Workspace.Content;
using Bocchi.Workspace.Content.Loaders;
using Bocchi.Workspace.Scanning;

namespace Bocchi.Workspace.Tests;

public sealed class LoaderTests
{
    private static readonly MarkdownPipeline Markdown = new();

    private static ContentLocation Loc(string path) => new("/cs", path);

    [Fact]
    public void PostLoader_ParsesValidPost()
    {
        var raw = """
            ---
            title: Hello World
            slug: hello-world
            publishedAt: 2025-03-14T10:00:00+08:00
            tags: [intro, blog]
            categories: [random]
            ---

            正文第一段。

            第二段，引用 ![cover](assets/cover.jpg)。
            """;
        var loader = new PostLoader(Markdown);

        var result = loader.Load(Loc("posts/2025/hello-world/index.md"), "2025", "hello-world", raw, TimeSpan.FromHours(8));

        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
        result.Document!.Frontmatter.Title.Should().Be("Hello World");
        result.Document.Frontmatter.Slug.Should().Be("hello-world");
        result.Document.Frontmatter.PublishedAt.Should().Be(new DateTimeOffset(2025, 3, 14, 10, 0, 0, TimeSpan.FromHours(8)));
        result.Document.Frontmatter.Tags.Should().BeEquivalentTo("intro", "blog");
        result.Document.Body.Html.Should().Contain("<p>");
        result.Document.Body.ReferencedMedia.Should().ContainSingle(m => m.Path == "assets/cover.jpg");
    }

    [Fact]
    public void PostLoader_ReportsMissingTitleAsError()
    {
        var raw = "---\nslug: x\n---\nbody\n";
        var loader = new PostLoader(Markdown);

        var result = loader.Load(Loc("posts/2025/x/index.md"), "2025", "x", raw, TimeSpan.FromHours(8));

        result.Document.Should().BeNull();
        result.Errors.Should().Contain(e => e.Code == "POST_MISSING_TITLE" && e.Severity == ContentErrorSeverity.Error);
    }

    [Fact]
    public void PostLoader_ReportsInvalidYaml()
    {
        var raw = "---\ntitle: [unterminated\n---\nbody";
        var loader = new PostLoader(Markdown);

        var result = loader.Load(Loc("posts/2025/x/index.md"), "2025", "x", raw, TimeSpan.FromHours(8));

        result.Document.Should().BeNull();
        result.Errors.Should().Contain(e => e.Code == "POST_INVALID_YAML");
    }

    [Fact]
    public void NoteLoader_ParsesFileNameDateAndSlug()
    {
        var raw = "今天阳光不错。";
        var loader = new NoteLoader(Markdown);

        var result = loader.Load(Loc("notes/2025/2025-03-14-1230-sunny.md"), "2025",
            "2025-03-14-1230-sunny.md", raw, TimeSpan.FromHours(8));

        result.Document.Should().NotBeNull();
        result.Document!.Frontmatter.PublishedAt.Should().Be(
            new DateTimeOffset(2025, 3, 14, 12, 30, 0, TimeSpan.FromHours(8)));
        result.Document.Frontmatter.Text.Should().Be("今天阳光不错。");
    }

    [Fact]
    public void NoteLoader_RejectsEmptyBody()
    {
        var raw = "---\nid: x\n---\n   \n";
        var loader = new NoteLoader(Markdown);

        var result = loader.Load(Loc("notes/2025/x.md"), "2025", "x.md", raw, TimeSpan.Zero);

        result.Document.Should().BeNull();
        result.Errors.Should().Contain(e => e.Code == "NOTE_EMPTY_BODY");
    }

    [Fact]
    public void FriendLinksLoader_ParsesShortAndFullForms()
    {
        var yaml = """
            friends:
              - name: Alice
                url: https://alice.example
              - name: Bob
                url: https://bob.example
                description: Bob's blog
                tags: [tech]
            """;

        var result = FriendLinksLoader.Load(Loc("friends/friends.yaml"), yaml);

        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
        result.Document!.Should().HaveCount(2);
        result.Document![1].Description.Should().Be("Bob's blog");
        result.Document![1].Tags.Should().ContainSingle(t => t == "tech");
    }

    [Fact]
    public void FriendLinksLoader_RejectsItemWithoutNameOrUrl()
    {
        var yaml = "friends:\n  - name: Alice\n";

        var result = FriendLinksLoader.Load(Loc("friends/friends.yaml"), yaml);

        result.Errors.Should().Contain(e => e.Code == "FRIEND_MISSING_FIELD");
    }

    [Fact]
    public void SiteSettingsLoader_RequiresTitleAndFallsBackWhenBaseUrlIsMissing()
    {
        var yaml = "language: en\n";

        var result = SiteSettingsLoader.Load(Loc("site/site.yaml"), yaml, null, null);

        result.Document.Should().BeNull();
        result.Errors.Should().Contain(e => e.Code == "SITE_MISSING_TITLE");
        result.Errors.Should().NotContain(e => e.Code == "SITE_MISSING_BASEURL");
    }

    [Fact]
    public void SiteSettingsLoader_UsesLocalhostFallbackWhenBaseUrlIsBlank()
    {
        var yaml = """
            title: Demo
            baseUrl: ""
            """;

        var result = SiteSettingsLoader.Load(Loc("site/site.yaml"), yaml, null, null);

        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
        result.Document!.BaseUrl.Should().Be(new Uri("http://localhost/"));
    }

    [Fact]
    public void SiteSettingsLoader_PrefersNavigationFileOverInlineNavigation()
    {
        var siteYaml = """
            title: Demo
            defaultTitle: Demo Default
            baseUrl: https://demo.example/
            copyright: Copyright © 2026 Demo.
            navigation:
              - title: A
                href: /a
            """;
        var navYaml = """
            items:
              - title: B
                href: /b
              - title: C
                href: /c
            """;

        var result = SiteSettingsLoader.Load(
            Loc("site/site.yaml"), siteYaml,
            Loc("site/navigation.yaml"), navYaml);

        result.Errors.Should().BeEmpty();
        result.Document!.DefaultTitle.Should().Be("Demo Default");
        result.Document.CopyrightNotice.Should().Be("Copyright © 2026 Demo.");
        result.Document!.Navigation.Should().HaveCount(2);
        result.Document.Navigation[0].Title.Should().Be("B");
    }
}
