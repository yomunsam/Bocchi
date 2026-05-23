using System.Text;

using Microsoft.Extensions.DependencyInjection;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Tests;

public sealed class EditorDraftServiceTests
{
    [Fact]
    public async Task CreateAsync_KeepsFileBackedDraftsIsolatedAndNoteSingleton()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();

        var post = await drafts.CreateAsync(ContentKind.Post);
        var page = await drafts.CreateAsync(ContentKind.Page);
        var work = await drafts.CreateAsync(ContentKind.Work);
        var note1 = await drafts.CreateAsync(ContentKind.Note);
        var note2 = await drafts.CreateAsync(ContentKind.Note);

        post.DraftId.Should().NotBe(page.DraftId);
        page.DraftId.Should().NotBe(work.DraftId);
        note1.DraftId.Should().Be("note");
        note2.DraftId.Should().Be("note");
        File.Exists(Path.Combine(factory.DataRoot, "state", "editor-drafts", post.DraftId, "content.md")).Should().BeTrue();
        Directory.EnumerateFiles(Path.Combine(factory.DataRoot, "workspace"), "index.md", SearchOption.AllDirectories)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task MoveAssetToDraftAsync_DeDuplicatesInsideOneDraftOnly()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var post = await drafts.CreateAsync(ContentKind.Post);
        var page = await drafts.CreateAsync(ContentKind.Page);

        var first = await drafts.MoveAssetToDraftAsync(post.DraftId, new MemoryStream([1]), "../Cover Image.PNG");
        var second = await drafts.MoveAssetToDraftAsync(post.DraftId, new MemoryStream([2]), "Cover Image.PNG");
        var pageAsset = await drafts.MoveAssetToDraftAsync(page.DraftId, new MemoryStream([3]), "Cover Image.PNG");

        first.Should().Be("assets/cover-image.png");
        second.Should().Be("assets/cover-image-2.png");
        pageAsset.Should().Be("assets/cover-image.png");
    }

    [Fact]
    public async Task CreateFromDraftAsync_MovesAssetsToFinalContentDirectory()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);
        var assetReference = await drafts.MoveAssetToDraftAsync(
            draft.DraftId,
            new MemoryStream(Encoding.UTF8.GetBytes("image")),
            "Cover.png");

        var saved = await editor.CreateFromDraftAsync(
            ContentKind.Post,
            "title: Asset Post\nslug: asset-post\nstatus: draft",
            $"Body ![cover]({assetReference})\n",
            draft.AssetsDirectory);

        saved.RelativePath.Should().EndWith("asset-post/index.md");
        saved.Markdown.Should().Contain("assets/cover.png");
        var finalAsset = Path.Combine(
            factory.DataRoot,
            "workspace",
            Path.GetDirectoryName(saved.RelativePath)!,
            "assets",
            "cover.png");
        File.Exists(finalAsset).Should().BeTrue();
        Directory.Exists(draft.AssetsDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesIndexDirectoryAndAssets()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();
        var contentDirectory = Path.Combine(factory.DataRoot, "workspace", "pages", "delete-me");
        var assetsDirectory = Path.Combine(contentDirectory, "assets");
        Directory.CreateDirectory(assetsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(contentDirectory, "index.md"),
            "---\ntitle: Delete Me\nslug: delete-me\nstatus: draft\n---\nBody\n");
        await File.WriteAllTextAsync(Path.Combine(assetsDirectory, "cover.txt"), "asset");

        await editor.DeleteAsync("pages/delete-me/index.md");

        Directory.Exists(contentDirectory).Should().BeFalse();
    }
}
