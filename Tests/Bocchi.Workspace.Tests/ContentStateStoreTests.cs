using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

namespace Bocchi.Workspace.Tests;

public sealed class ContentStateStoreTests
{
    private static (TempWorkspace temp, ContentStateStore store) NewStore()
    {
        var temp = new TempWorkspace();
        Directory.CreateDirectory(temp.Layout.BocchiDirectory);
        var factory = new SqliteConnectionFactory(temp.Layout);
        new SchemaMigrator(factory).MigrateAsync().GetAwaiter().GetResult();
        return (temp, new ContentStateStore(factory));
    }

    [Fact]
    public async Task SchemaMigrator_IsIdempotent()
    {
        using var temp = new TempWorkspace();
        Directory.CreateDirectory(temp.Layout.BocchiDirectory);
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