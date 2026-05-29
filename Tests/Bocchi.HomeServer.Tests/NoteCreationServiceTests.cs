using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace.State;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

public sealed class NoteCreationServiceTests
{
    [Fact]
    public async Task CreateAsync_WritesDirectoryNoteWithStableShortId()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var notes = scope.ServiceProvider.GetRequiredService<NoteCreationService>();
        var store = scope.ServiceProvider.GetRequiredService<IContentStateStore>();

        var relativePath = await notes.CreateAsync("新短文正文");

        relativePath.Should().MatchRegex("^notes/\\d{4}/\\d{4}/\\d{4}-[a-z0-9]{8}/index\\.md$");
        var parts = relativePath.Split('/');
        var id = parts[3][5..];
        var fullPath = Path.Combine(factory.DataRoot, "workspace", relativePath.Replace('/', Path.DirectorySeparatorChar));
        var raw = await File.ReadAllTextAsync(fullPath);

        raw.Should().Contain($"id: {id}");
        raw.Should().Contain("publishedAt: ");
        raw.Should().Contain("status: published");
        raw.Should().Contain("新短文正文");
        raw.Should().NotContain("slug:");
        Directory.Exists(Path.Combine(Path.GetDirectoryName(fullPath)!, "assets")).Should().BeTrue();

        var summaries = await store.ListContentSummariesAsync(ContentKind.Note);
        summaries.Should().ContainSingle(s =>
            s.ContentId == id &&
            s.RelativePath == relativePath &&
            s.Year == parts[1]);
    }
}
