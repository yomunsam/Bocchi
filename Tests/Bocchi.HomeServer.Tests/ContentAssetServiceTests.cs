using System.Text;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

public sealed class ContentAssetServiceTests
{
    [Fact]
    public async Task UploadDraftAssetAsync_NormalizesAndDeDuplicatesFileName()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var assets = scope.ServiceProvider.GetRequiredService<ContentAssetService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);

        var first = await assets.UploadDraftAssetAsync(
            draft.DraftId,
            CreateStream("first"),
            "Cover Image.PNG",
            "image/png");
        var second = await assets.UploadDraftAssetAsync(
            draft.DraftId,
            CreateStream("second"),
            "Cover Image.PNG",
            "image/png");

        first.RelativePath.Should().Be("assets/cover-image.png");
        first.ContentType.Should().Be("image/png");
        first.Category.Should().Be(ContentAssetCategory.Image);
        second.RelativePath.Should().Be("assets/cover-image-2.png");
        File.Exists(Path.Combine(draft.AssetsDirectory, "cover-image.png")).Should().BeTrue();
        File.Exists(Path.Combine(draft.AssetsDirectory, "cover-image-2.png")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadContentAssetAsync_WritesIntoSavedContentAssetsDirectory()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();
        var assets = scope.ServiceProvider.GetRequiredService<ContentAssetService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);
        var saved = await editor.CreateFromDraftAsync(
            ContentKind.Post,
            "title: Asset Post\nslug: asset-post\nstatus: draft",
            "Body\n",
            draft.AssetsDirectory);
        var variantPath = Path.Combine(Path.GetDirectoryName(saved.RelativePath)!, "index.en-US.md").Replace('\\', '/');
        var variant = await editor.CreateLanguageVariantAsync(
            variantPath,
            "title: Asset Post\nslug: asset-post\nstatus: draft\nlanguage: en-US",
            "Body\n");

        var uploaded = await assets.UploadContentAssetAsync(
            variant.RelativePath,
            CreateStream("manual"),
            "Manual.PDF",
            "application/pdf");
        var listed = await assets.ListContentAssetsAsync(
            saved.RelativePath,
            string.Empty,
            "[manual](assets/manual.pdf)");

        uploaded.RelativePath.Should().Be("assets/manual.pdf");
        uploaded.ContentType.Should().Be("application/pdf");
        uploaded.Category.Should().Be(ContentAssetCategory.Attachment);
        listed.Should().Contain(x => x.RelativePath == "assets/manual.pdf" && x.Referenced);
        File.Exists(Path.Combine(
            factory.DataRoot,
            "workspace",
            Path.GetDirectoryName(saved.RelativePath)!,
            "assets",
            "manual.pdf")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAsync_RejectsTraversalAndUnsupportedTypes()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var assets = scope.ServiceProvider.GetRequiredService<ContentAssetService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);

        var traversal = () => assets.UploadDraftAssetAsync(
                draft.DraftId,
                CreateStream("bad"),
                "../evil.png",
                "image/png");
        await traversal.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*文件名不能包含路径*");
        var svg = () => assets.UploadDraftAssetAsync(
                draft.DraftId,
                CreateStream("<svg />"),
                "icon.svg",
                "image/svg+xml");
        await svg.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*不支持的资产文件类型*");
    }

    [Fact]
    public async Task ListDraftAssetsAsync_MarksMarkdownHtmlAndFrontmatterReferences()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var assets = scope.ServiceProvider.GetRequiredService<ContentAssetService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("cover"), "cover.jpg", "image/jpeg");
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("image"), "markdown-image.png", "image/png");
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("html"), "html-image.webp", "image/webp");
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("metadata"), "metadata.pdf", "application/pdf");
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("metadata-object"), "metadata-object.pdf", "application/pdf");
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("manual"), "manual.pdf", "application/pdf");
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("unused"), "unused.png", "image/png");

        var listed = await assets.ListDraftAssetsAsync(
            draft.DraftId,
            """
            cover: assets/cover.jpg
            media:
              - assets/metadata.pdf
              - path: assets/metadata-object.pdf
            """,
            """
            ![inline](assets/markdown-image.png)
            [manual](assets/manual.pdf)
            <img src="assets/html-image.webp" alt="">
            """);

        listed.Should().Contain(x => x.RelativePath == "assets/cover.jpg" && x.Referenced);
        listed.Should().Contain(x => x.RelativePath == "assets/markdown-image.png" && x.Referenced);
        listed.Should().Contain(x => x.RelativePath == "assets/html-image.webp" && x.Referenced);
        listed.Should().Contain(x => x.RelativePath == "assets/metadata.pdf" && x.Referenced);
        listed.Should().Contain(x => x.RelativePath == "assets/metadata-object.pdf" && x.Referenced);
        listed.Should().Contain(x => x.RelativePath == "assets/manual.pdf" && x.Referenced);
        listed.Should().Contain(x => x.RelativePath == "assets/unused.png" && !x.Referenced);
    }

    [Fact]
    public async Task ContentAssets_AggregateReferencesFromSiblingLanguageVariants()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();
        var assets = scope.ServiceProvider.GetRequiredService<ContentAssetService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);
        var saved = await editor.CreateFromDraftAsync(
            ContentKind.Post,
            "title: Multilingual Asset\nslug: multilingual-asset\nstatus: draft",
            "Primary body\n",
            draft.AssetsDirectory);
        var variantPath = Path.Combine(Path.GetDirectoryName(saved.RelativePath)!, "index.en-US.md").Replace('\\', '/');
        await editor.CreateLanguageVariantAsync(
            variantPath,
            "title: Multilingual Asset\nslug: multilingual-asset\nstatus: draft\nlanguage: en-US",
            "English body ![only english](assets/english-only.png)\n");
        var uploaded = await assets.UploadContentAssetAsync(
            saved.RelativePath,
            CreateStream("english"),
            "english-only.png",
            "image/png");

        var listed = await assets.ListContentAssetsAsync(
            saved.RelativePath,
            saved.Yaml,
            "Primary body without asset\n");
        var deleteReferenced = () => assets.DeleteContentAssetAsync(
            saved.RelativePath,
            uploaded.RelativePath,
            saved.Yaml,
            "Primary body without asset\n");

        listed.Should().Contain(x => x.RelativePath == uploaded.RelativePath && x.Referenced);
        await deleteReferenced.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*仍被当前内容引用*");
    }

    [Fact]
    public async Task DeleteAssetAsync_RejectsReferencedAssetAndDeletesUnusedAsset()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var assets = scope.ServiceProvider.GetRequiredService<ContentAssetService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("used"), "used.png", "image/png");
        await assets.UploadDraftAssetAsync(draft.DraftId, CreateStream("unused"), "unused.txt", "text/plain");

        var deleteReferenced = () => assets.DeleteDraftAssetAsync(
                draft.DraftId,
                "assets/used.png",
                string.Empty,
                "![used](assets/used.png)");
        await deleteReferenced.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*仍被当前内容引用*");
        var deleteTraversal = () => assets.DeleteDraftAssetAsync(
            draft.DraftId,
            "assets/../used.png",
            string.Empty,
            string.Empty);
        await deleteTraversal.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*assets/*");

        await assets.DeleteDraftAssetAsync(
            draft.DraftId,
            "assets/unused.txt",
            string.Empty,
            "![used](assets/used.png)");

        File.Exists(Path.Combine(draft.AssetsDirectory, "used.png")).Should().BeTrue();
        File.Exists(Path.Combine(draft.AssetsDirectory, "unused.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteContentAssetAsync_UsesSavedContentGroupAssetsDirectory()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var drafts = scope.ServiceProvider.GetRequiredService<EditorDraftService>();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();
        var assets = scope.ServiceProvider.GetRequiredService<ContentAssetService>();
        var draft = await drafts.CreateAsync(ContentKind.Page);
        var saved = await editor.CreateFromDraftAsync(
            ContentKind.Page,
            "title: About Assets\nslug: about-assets\nstatus: draft",
            "Body\n",
            draft.AssetsDirectory);
        var uploaded = await assets.UploadContentAssetAsync(
            saved.RelativePath,
            CreateStream("unused"),
            "unused.txt",
            "text/plain");

        await assets.DeleteContentAssetAsync(saved.RelativePath, uploaded.RelativePath, string.Empty, string.Empty);

        File.Exists(Path.Combine(
            factory.DataRoot,
            "workspace",
            Path.GetDirectoryName(saved.RelativePath)!,
            uploaded.RelativePath)).Should().BeFalse();
    }

    private static MemoryStream CreateStream(string text)
        => new(Encoding.UTF8.GetBytes(text));
}
