using System.Net;
using System.Text.Json;

namespace Bocchi.HomeServer.Tests;

public sealed class BuildPageTests : IClassFixture<IsolatedDataRootWebApplicationFactory>
{
    private readonly IsolatedDataRootWebApplicationFactory _factory;

    public BuildPageTests(IsolatedDataRootWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BuildPage_RendersBuildSurface()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Publish");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("publish target");
        body.Should().Contain("Local output");
        body.Should().Contain("/Admin/Publish/download");
    }

    [Fact]
    public async Task LocalOutputPage_RendersPublishSubNavigation()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Publish/LocalOutput");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Local output");
        body.Should().Contain("aria-current=\"page\"");
        body.Should().Contain("/Admin/Publish");
    }

    [Fact]
    public async Task BuildRunEndpoint_ProducesDownloadableZip()
    {
        _factory.SeedPublishedPostWithMedia();
        using var client = await _factory.CreateAdminClientAsync();

        var run = await client.PostAsync("/Admin/Publish/run", content: null);
        run.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await run.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Succeeded");
        doc.RootElement.GetProperty("artifactCount").GetInt32().Should().BeGreaterThan(0);

        var download = await client.GetAsync("/Admin/Publish/download");
        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        var bytes = await download.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith([0x50, 0x4B]);
    }

    [Fact]
    public async Task Download_Returns404BeforeFirstBuild()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = await factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Publish/download");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
