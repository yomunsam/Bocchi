using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

namespace Bocchi.Workspace.Tests;

public sealed class ContentStateStoreTests
{
    private static (TempDataRoot temp, ContentStateStore store) NewStore()
    {
        var temp = new TempDataRoot();
        Directory.CreateDirectory(temp.Layout.StateDirectory);
        var factory = new SqliteConnectionFactory(temp.Layout);
        new SchemaMigrator(factory).MigrateAsync().GetAwaiter().GetResult();
        return (temp, new ContentStateStore(factory));
    }

    [Fact]
    public async Task SchemaMigrator_IsIdempotent()
    {
        using var temp = new TempDataRoot();
        Directory.CreateDirectory(temp.Layout.StateDirectory);
        var factory = new SqliteConnectionFactory(temp.Layout);
        var migrator = new SchemaMigrator(factory);

        var first = await migrator.MigrateAsync();
        var second = await migrator.MigrateAsync();

        first.Should().Be(SchemaMigrator.CurrentVersion);
        second.Should().Be(SchemaMigrator.CurrentVersion);
    }

    [Fact]
    public async Task UpsertContentItem_OverwritesOnSameKindAndId()
    {
        var (temp, store) = NewStore();
        using (temp)
        {
            var fileId = await store.UpsertFileAsync(new FileUpsert(
                "posts/2025/x/index.md", ContentKind.Post, "abc", DateTimeOffset.UtcNow));

            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.Post, "x", "x", "Title v1", ContentStatus.Draft, "2025",
                null, null, null, "posts/2025/x/index.md"), fileId);
            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.Post, "x", "x", "Title v2", ContentStatus.Published, "2025",
                null, null, null, "posts/2025/x/index.md"), fileId);

            var items = await store.ListContentSummariesAsync(ContentKind.Post);
            items.Should().HaveCount(1);
            items[0].Title.Should().Be("Title v2");
            items[0].Status.Should().Be(ContentStatus.Published);
        }
    }

    [Fact]
    public async Task UpsertContentItem_ReplacesOldSlugForSameSourceFile()
    {
        var (temp, store) = NewStore();
        using (temp)
        {
            var fileId = await store.UpsertFileAsync(new FileUpsert(
                "posts/2025/old/index.md", ContentKind.Post, "abc", DateTimeOffset.UtcNow));

            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.Post, "old", "old", "Title", ContentStatus.Draft, "2025",
                null, null, null, "posts/2025/old/index.md"), fileId);
            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.Post, "new", "new", "Title", ContentStatus.Draft, "2025",
                null, null, null, "posts/2025/old/index.md"), fileId);

            var items = await store.ListContentSummariesAsync(ContentKind.Post);
            items.Should().ContainSingle();
            items[0].ContentId.Should().Be("new");
        }
    }

    [Fact]
    public async Task UpsertContentItem_KeepsMultipleFriendLinksForSameSourceFile()
    {
        var (temp, store) = NewStore();
        using (temp)
        {
            var fileId = await store.UpsertFileAsync(new FileUpsert(
                "friends/friends.yaml", ContentKind.FriendLink, "abc", DateTimeOffset.UtcNow));

            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.FriendLink, "https://a.example", null, "A", ContentStatus.Published, null,
                null, null, null, "friends/friends.yaml"), fileId);
            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.FriendLink, "https://b.example", null, "B", ContentStatus.Published, null,
                null, null, null, "friends/friends.yaml"), fileId);

            var items = await store.ListContentSummariesAsync(ContentKind.FriendLink);
            items.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task UpsertContentItem_PersistsLocalizationMetadata()
    {
        var (temp, store) = NewStore();
        using (temp)
        {
            var fileId = await store.UpsertFileAsync(new FileUpsert(
                "posts/2025/hello/index.zh-TW.md", ContentKind.Post, "abc", DateTimeOffset.UtcNow));

            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.Post,
                "posts/2025/hello@zh-TW",
                "hello",
                "你好繁中",
                ContentStatus.Published,
                "2025",
                null,
                null,
                null,
                "posts/2025/hello/index.zh-TW.md",
                Language: "zh-TW",
                LocalizationGroup: "posts/2025/hello",
                IsTranslation: true,
                SourceLanguage: "zh-CN",
                SourceContentId: "posts/2025/hello@zh-CN"), fileId);

            var items = await store.ListContentSummariesAsync(ContentKind.Post);

            items.Should().ContainSingle();
            items[0].Language.Should().Be("zh-TW");
            items[0].LocalizationGroup.Should().Be("posts/2025/hello");
            items[0].IsTranslation.Should().BeTrue();
            items[0].SourceLanguage.Should().Be("zh-CN");
            items[0].SourceContentId.Should().Be("posts/2025/hello@zh-CN");
        }
    }

    [Fact]
    public async Task ScanRunFlow_RecordsMetadataAndErrors()
    {
        var (temp, store) = NewStore();
        using (temp)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var runId = await store.StartScanRunAsync(startedAt, gitHeadSha: "deadbeef");

            await store.AppendErrorsAsync(runId, [
                new ContentValidationError("posts/2025/x/index.md", ContentKind.Post, "title",
                    ContentErrorSeverity.Error, "POST_MISSING_TITLE", "missing"),
            ]);

            await store.FinishScanRunAsync(runId, startedAt.AddSeconds(1), 5, 4, 1, 0, "succeeded");

            var latest = await store.GetLatestScanRunAsync();
            latest.Should().NotBeNull();
            latest!.Status.Should().Be("succeeded");
            latest.GitHeadSha.Should().Be("deadbeef");
            latest.ErrorCount.Should().Be(1);

            var errors = await store.ListErrorsAsync(runId);
            errors.Should().ContainSingle();
            errors[0].Code.Should().Be("POST_MISSING_TITLE");
        }
    }
}
