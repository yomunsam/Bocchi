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
    private static async Task<(TempDataRoot temp, ContentScanner scanner, IContentStateStore store)> NewScannerAsync()
    {
        var temp = new TempDataRoot();
        await new BocchiDataInitializer(temp.Layout).InitializeAsync();
        var factory = new SqliteConnectionFactory(temp.Layout);
        await new SchemaMigrator(factory).MigrateAsync();
        var store = new ContentStateStore(factory);
        var md = new MarkdownPipeline();
        var repo = new LibGit2ContentRepository(temp.Layout.Workspace);
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
            var cs = temp.Layout.Workspace;

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
            var noteDir = Path.Combine(cs.NotesDirectory, "2025", "0314", "1230-k7p9xq2m");
            await WriteAsync(Path.Combine(noteDir, "index.md"),
                "---\nid: k7p9xq2m\nstatus: Published\npublishedAt: 2025-03-14T12:30:00+08:00\n---\n短文一条。\n");

            var result = await scanner.ScanAsync();

            result.Posts.Should().ContainSingle(p => p.Frontmatter.Slug == "hello");
            result.Pages.Should().ContainSingle(p => p.Frontmatter.Slug == "about");
            result.Works.Should().ContainSingle(w => w.Frontmatter.Slug == "alpha");
            result.Notes.Should().ContainSingle(n => n.Frontmatter.Id == "k7p9xq2m");
            result.SiteSettings.Should().NotBeNull(); // default site.yaml from initializer
            result.HasErrors.Should().BeFalse();

            var summaries = await store.ListContentSummariesAsync(null);
            summaries.Should().Contain(s => s.Kind == ContentKind.Post && s.ContentId == "posts/2025/hello@zh-CN");
            summaries.Should().Contain(s => s.Kind == ContentKind.Page && s.ContentId == "pages/about@zh-CN");
            summaries.Should().Contain(s => s.Kind == ContentKind.Work && s.ContentId == "works/2024/alpha@zh-CN");
            summaries.Should().Contain(s => s.Kind == ContentKind.Note && s.ContentId == "k7p9xq2m");
            summaries.Should().Contain(s => s.Kind == ContentKind.SiteSettings && s.RelativePath.EndsWith("site.yaml", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Scan_ValidatesDirectoryNoteIdsAndRejectsLegacyFiles()
    {
        var (temp, scanner, _) = await NewScannerAsync();
        using (temp)
        {
            var notes = temp.Layout.Workspace.NotesDirectory;
            await WriteAsync(
                Path.Combine(notes, "2025", "0314", "1230-k7p9xq2m", "index.md"),
                "---\nid: k7p9xq2m\nstatus: Published\npublishedAt: 2025-03-14T12:30:00+08:00\n---\n短文一条。\n");
            await WriteAsync(
                Path.Combine(notes, "2025", "0314", "1231-k7p9xq2m", "index.md"),
                "---\nid: k7p9xq2m\nstatus: Published\npublishedAt: 2025-03-14T12:31:00+08:00\n---\n重复 id。\n");
            await WriteAsync(
                Path.Combine(notes, "2025", "0314", "1232-a1b2c3d4", "index.md"),
                "---\nid: z9y8x7w6\nstatus: Published\npublishedAt: 2025-03-14T12:32:00+08:00\n---\n目录 id 不一致。\n");
            await WriteAsync(
                Path.Combine(notes, "2025", "2025-03-14-1233-old.md"),
                "旧单文件短文。\n");

            var result = await scanner.ScanAsync();

            result.Notes.Should().ContainSingle(n => n.Frontmatter.Id == "k7p9xq2m");
            result.Errors.Should().Contain(e => e.Code == "NOTE_DUPLICATE_ID");
            result.Errors.Should().Contain(e => e.Code == "NOTE_ID_DIRECTORY_MISMATCH");
            result.Errors.Should().Contain(e => e.Code == "NOTE_LEGACY_FILE_UNSUPPORTED");
        }
    }

    [Fact]
    public async Task Scan_LoadsLanguageVariantsAndPersistsLocalizationState()
    {
        var (temp, scanner, store) = await NewScannerAsync();
        using (temp)
        {
            var postDir = Path.Combine(temp.Layout.Workspace.PostsDirectory, "2025", "hello");
            await WriteAsync(Path.Combine(postDir, "index.md"),
                "---\ntitle: 你好\nslug: hello\nstatus: Published\n---\n中文正文。\n");
            await WriteAsync(Path.Combine(postDir, "index.zh-TW.md"),
                "---\ntitle: 你好繁中\nslug: hello\nstatus: Published\nlanguage: zh-TW\nlocalization:\n  translationOf:\n    language: zh-CN\n---\n繁中正文。\n");

            var result = await scanner.ScanAsync();

            result.HasErrors.Should().BeFalse();
            result.Posts.Should().HaveCount(2);
            result.Posts.Should().Contain(post => post.Frontmatter.Language == "zh-CN");
            result.Posts.Should().Contain(post =>
                post.Frontmatter.Language == "zh-TW" &&
                post.Frontmatter.Localization!.TranslationOf!.Language == "zh-CN");

            var summaries = await store.ListContentSummariesAsync(ContentKind.Post);
            summaries.Should().Contain(summary =>
                summary.ContentId == "posts/2025/hello@zh-CN" &&
                summary.Language == "zh-CN" &&
                summary.LocalizationGroup == "posts/2025/hello" &&
                !summary.IsTranslation);
            summaries.Should().Contain(summary =>
                summary.ContentId == "posts/2025/hello@zh-TW" &&
                summary.Language == "zh-TW" &&
                summary.LocalizationGroup == "posts/2025/hello" &&
                summary.IsTranslation &&
                summary.SourceContentId == "posts/2025/hello@zh-CN");
        }
    }

    [Fact]
    public async Task Scan_ReportsMissingMediaAsError()
    {
        var (temp, scanner, _) = await NewScannerAsync();
        using (temp)
        {
            var postDir = Path.Combine(temp.Layout.Workspace.PostsDirectory, "2025", "broken");
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
            var bad = Path.Combine(temp.Layout.Workspace.PostsDirectory, "twentyfive", "x");
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
            var postDir = Path.Combine(temp.Layout.Workspace.PostsDirectory, "2025", "deriv");
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
