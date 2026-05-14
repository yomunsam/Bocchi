using Bocchi.ContentModel;
using Bocchi.Workspace.Content;
using Bocchi.Workspace.Content.Loaders;
using Bocchi.Workspace.Git;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bocchi.Workspace.Tests;

public sealed class ContentScannerTests
{
    private static async Task<(TempWorkspace temp, ContentScanner scanner, IContentStateStore store)> NewScannerAsync()
    {
        var temp = new TempWorkspace();
        await new WorkspaceInitializer(temp.Layout).InitializeAsync();
        var factory = new SqliteConnectionFactory(temp.Layout);
        await new SchemaMigrator(factory).MigrateAsync();
        var store = new ContentStateStore(factory);
        var md = new MarkdownPipeline();
        var repo = new LibGit2ContentRepository(temp.Layout.ContentSpace);
        var scanner = new ContentScanner(
            temp.Layout, store, repo,
            new PostLoader(md), new PageLoader(md), new WorkLoader(md), new NoteLoader(md),
            TimeProvider.System, NullLogger<ContentScanner>.Instance);
        return (temp, scanner, store);
    }

    private static async Task WriteAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    [Fact]
    public async Task Scan_LoadsAllKindsAndPersistsState()
    {
        var (temp, scanner, store) = await NewScannerAsync();
        using (temp)
        {
            var cs = temp.Layout.ContentSpace;

            // Post
            var postDir = Path.Combine(cs.PostsDirectory, "2025", "hello");
            Directory.CreateDirectory(Path.Combine(postDir, "assets"));
            await WriteAsync(Path.Combine(postDir, "index.md"),
                "---\ntitle: Hello\nslug: hello\npublishedAt: 2025-03-14\n---\nbody ![c](assets/c.jpg)\n");
            await File.WriteAllBytesAsync(Path.Combine(postDir, "assets", "c.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });

            // Page
            var pageDir = Path.Combine(cs.PagesDirectory, "about");
            Directory.CreateDirectory(pageDir);
            await WriteAsync(Path.Combine(pageDir, "index.md"),
                "---\ntitle: About\nslug: about\n---\nAbout me.\n");

            // Work
            var workDir = Path.Combine(cs.WorksDirectory, "2024", "alpha");
            Directory.CreateDirectory(workDir);
            await WriteAsync(Path.Combine(workDir, "index.md"),
                "---\ntitle: Alpha\nslug: alpha\n---\ndesc\n");

            // Note
            var noteFile = Path.Combine(cs.NotesDirectory, "2025", "2025-03-14-1230-hi.md");
            await WriteAsync(noteFile, "短文一条。");

            var result = await scanner.ScanAsync();

            result.Posts.Should().ContainSingle(p => p.Frontmatter.Slug == "hello");
            result.Pages.Should().ContainSingle(p => p.Frontmatter.Slug == "about");
            result.Works.Should().ContainSingle(w => w.Frontmatter.Slug == "alpha");
            result.Notes.Should().ContainSingle();
            result.SiteSettings.Should().NotBeNull(); // default site.yaml from initializer
            result.HasErrors.Should().BeFalse();

            var summaries = await store.ListContentSummariesAsync(null);
            summaries.Should().Contain(s => s.Kind == ContentKind.Post && s.ContentId == "hello");
            summaries.Should().Contain(s => s.Kind == ContentKind.Page && s.ContentId == "about");
            summaries.Should().Contain(s => s.Kind == ContentKind.Work && s.ContentId == "alpha");
        }
    }

    [Fact]
    public async Task Scan_ReportsMissingMediaAsError()
    {
        var (temp, scanner, _) = await NewScannerAsync();
        using (temp)
        {
            var postDir = Path.Combine(temp.Layout.ContentSpace.PostsDirectory, "2025", "broken");
            Directory.CreateDirectory(postDir);
            await WriteAsync(Path.Combine(postDir, "index.md"),
                "---\ntitle: Broken\nslug: broken\n---\n![missing](assets/nope.jpg)\n");

            var result = await scanner.ScanAsync();

            result.Errors.Should().Contain(e => e.Code == "MEDIA_MISSING");
        }
    }

    [Fact]
    public async Task Scan_ReportsInvalidYearDirectory()
    {
        var (temp, scanner, _) = await NewScannerAsync();
        using (temp)
        {
            var bad = Path.Combine(temp.Layout.ContentSpace.PostsDirectory, "twentyfive", "x");
            Directory.CreateDirectory(bad);
            await WriteAsync(Path.Combine(bad, "index.md"),
                "---\ntitle: x\nslug: x\n---\nbody\n");

            var result = await scanner.ScanAsync();

            result.Errors.Should().Contain(e => e.Code == "INVALID_YEAR_DIR");
        }
    }

    [Fact]
    public async Task Scan_FlagsSuspiciousDerivativesInAssets()
    {
        var (temp, scanner, _) = await NewScannerAsync();
        using (temp)
        {
            var postDir = Path.Combine(temp.Layout.ContentSpace.PostsDirectory, "2025", "deriv");
            Directory.CreateDirectory(Path.Combine(postDir, "assets"));
            await WriteAsync(Path.Combine(postDir, "index.md"),
                "---\ntitle: D\nslug: d\n---\n![cover](assets/cover.jpg)\n");
            await File.WriteAllBytesAsync(Path.Combine(postDir, "assets", "cover.jpg"), [0xFF, 0xD8, 0xFF]);
            await File.WriteAllBytesAsync(Path.Combine(postDir, "assets", "cover.webp"), [0x00]);

            var result = await scanner.ScanAsync();

            result.Warnings.Should().Contain(w => w.Code == "SUSPICIOUS_DERIVATIVE");
        }
    }
}
