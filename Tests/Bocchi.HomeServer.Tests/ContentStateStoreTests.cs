using Bocchi.ContentModel;
using Bocchi.HomeServer.Data.State;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Tests;

public sealed class ContentStateStoreTests
{
    private static async Task<(StateTestDatabase temp, ContentStateStore store)> NewStoreAsync()
    {
        var temp = await StateTestDatabase.CreateAsync();
        return (temp, new ContentStateStore(temp, TimeProvider.System));
    }

    [Fact]
    public async Task InitialMigration_MatchesCurrentModel()
    {
        using var temp = await StateTestDatabase.CreateAsync();
        await using var db = temp.CreateDbContext();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        db.Database.HasPendingModelChanges().Should().BeFalse("模型改动后必须用 dotnet ef 生成新的 migration");
    }

    [Fact]
    public async Task DeleteContentBySourcePath_CascadesToContentItems()
    {
        var (temp, store) = await NewStoreAsync();
        using (temp)
        {
            var fileId = await store.UpsertFileAsync(new FileUpsert(
                "posts/2025/x/index.md", ContentKind.Post, "abc", DateTimeOffset.UtcNow));
            await store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.Post, "x", "x", "Title", ContentStatus.Draft, "2025",
                null, null, null, "posts/2025/x/index.md"), fileId);

            await store.DeleteContentBySourcePathAsync("posts\\2025\\x\\index.md");

            (await store.ListContentSummariesAsync(null)).Should().BeEmpty();
            await using var db = temp.CreateDbContext();
            (await db.ContentFiles.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task ListContentSummaries_OrdersByPublishedAtDescendingWithUndatedLast()
    {
        var (temp, store) = await NewStoreAsync();
        using (temp)
        {
            async Task AddAsync(string id, DateTimeOffset? publishedAt)
            {
                var fileId = await store.UpsertFileAsync(new FileUpsert(
                    $"posts/2025/{id}/index.md", ContentKind.Post, "abc", DateTimeOffset.UtcNow));
                await store.UpsertContentItemAsync(new ContentItemUpsert(
                    ContentKind.Post, id, id, id, ContentStatus.Published, "2025",
                    publishedAt, null, null, $"posts/2025/{id}/index.md"), fileId);
            }

            await AddAsync("undated", null);
            await AddAsync("older", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(8)));
            await AddAsync("newer", new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

            var items = await store.ListContentSummariesAsync(ContentKind.Post);

            items.Select(x => x.ContentId).Should().Equal("newer", "older", "undated");
            items[1].PublishedAt.Should().Be(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.FromHours(8)));
            items[0].RelativePath.Should().Be("posts/2025/newer/index.md");
        }
    }

    [Fact]
    public async Task UpsertContentItem_OverwritesOnSameKindAndId()
    {
        var (temp, store) = await NewStoreAsync();
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
        var (temp, store) = await NewStoreAsync();
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
        var (temp, store) = await NewStoreAsync();
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
        var (temp, store) = await NewStoreAsync();
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
        var (temp, store) = await NewStoreAsync();
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
