using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

public sealed class ContentLanguageVersionServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesDraftLanguageVariantAndRefreshesState()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var (editor, versions, store, source) = await CreateSourcePostAsync(services, ["zh-CN", "zh-TW"]);

        var created = await versions.CreateAsync(new CreateContentLanguageVariantRequest(
            source.RelativePath,
            "zh-TW",
            CopyCurrentContent: true,
            IsTranslation: false,
            SourceContentId: null));

        created.RelativePath.Should().EndWith("hello-language/index.zh-TW.md");
        var file = await editor.ReadAsync(created.RelativePath);
        file.Yaml.Should().Contain("language: zh-TW");
        file.Yaml.Should().Contain("status: draft");
        file.Yaml.Should().Contain("group:");
        file.Yaml.Should().NotContain("translationOf:");
        file.Markdown.Should().Be(source.Markdown);

        var summaries = await store.ListContentSummariesAsync(ContentKind.Post);
        summaries.Should().Contain(summary =>
            summary.RelativePath == created.RelativePath &&
            summary.Language == "zh-TW" &&
            summary.LocalizationGroup != null &&
            !summary.IsTranslation);
    }

    [Fact]
    public async Task CreateAsync_WhenCopyIsDisabledKeepsTitleAndLeavesBodyEmpty()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var (editor, versions, _, source) = await CreateSourcePostAsync(services, ["zh-CN", "en-US"]);

        var created = await versions.CreateAsync(new CreateContentLanguageVariantRequest(
            source.RelativePath,
            "en-US",
            CopyCurrentContent: false,
            IsTranslation: false,
            SourceContentId: null));

        var file = await editor.ReadAsync(created.RelativePath);
        file.Yaml.Should().Contain("title: Hello Language");
        file.Yaml.Should().Contain("language: en-US");
        file.Markdown.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenTranslationWritesSourceAndContextResolvesIt()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var (editor, versions, store, source) = await CreateSourcePostAsync(services, ["zh-CN", "zh-TW"]);
        var before = await versions.GetAsync(source.RelativePath);

        var created = await versions.CreateAsync(new CreateContentLanguageVariantRequest(
            source.RelativePath,
            "zh-TW",
            CopyCurrentContent: true,
            IsTranslation: true,
            SourceContentId: before!.Current.ContentId));

        var file = await editor.ReadAsync(created.RelativePath);
        file.Yaml.Should().Contain("translationOf:");
        file.Yaml.Should().Contain("language: zh-CN");
        file.Yaml.Should().Contain($"contentId: {before.Current.ContentId}");

        var summaries = await store.ListContentSummariesAsync(ContentKind.Post);
        summaries.Should().Contain(summary =>
            summary.RelativePath == created.RelativePath &&
            summary.IsTranslation &&
            summary.SourceLanguage == "zh-CN" &&
            summary.SourceContentId == before.Current.ContentId);

        var after = await versions.GetAsync(created.RelativePath);
        after!.TranslationSource.Should().NotBeNull();
        after.TranslationSource!.ContentId.Should().Be(before.Current.ContentId);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateTargetLanguage()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var (_, versions, _, source) = await CreateSourcePostAsync(services, ["zh-CN", "zh-TW"]);
        await versions.CreateAsync(new CreateContentLanguageVariantRequest(
            source.RelativePath,
            "zh-TW",
            CopyCurrentContent: true,
            IsTranslation: false,
            SourceContentId: null));

        Func<Task> act = async () => await versions.CreateAsync(new CreateContentLanguageVariantRequest(
            source.RelativePath,
            "zh-TW",
            CopyCurrentContent: true,
            IsTranslation: false,
            SourceContentId: null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*已经存在目标语言版本*");
    }

    private static async Task<(ContentEditingService Editor, ContentLanguageVersionService Versions, IContentStateStore Store, EditableContentFile Source)> CreateSourcePostAsync(
        IServiceProvider services,
        string[] enabledLanguages)
    {
        var localization = services.GetRequiredService<LocalizationSettingsService>();
        await localization.SaveAsync("zh-CN", enabledLanguages, []);

        var editor = services.GetRequiredService<ContentEditingService>();
        var drafts = services.GetRequiredService<EditorDraftService>();
        var scanner = services.GetRequiredService<ContentScanner>();
        var store = services.GetRequiredService<IContentStateStore>();
        var versions = services.GetRequiredService<ContentLanguageVersionService>();
        var draft = await drafts.CreateAsync(ContentKind.Post);
        var source = await editor.CreateFromDraftAsync(
            ContentKind.Post,
            "title: Hello Language\nslug: hello-language\nstatus: published",
            "Original body\n",
            draft.AssetsDirectory);
        await scanner.ScanAsync();
        return (editor, versions, store, source);
    }
}
