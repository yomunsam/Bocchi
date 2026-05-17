using System.Net;
using System.Text.Json;

namespace Bocchi.HomeServer.Tests;

public sealed class PreviewEndpointTests : IClassFixture<IsolatedDataRootWebApplicationFactory>
{
    private readonly IsolatedDataRootWebApplicationFactory _factory;

    public PreviewEndpointTests(IsolatedDataRootWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PreviewDataEndpoint_ReturnsThemeInputJson()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/_bocchi/preview/data/site.json");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Headers.ETag!.Tag.Should().StartWith("\"sha256-");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("$schema").GetString().Should().Be("https://bocchi.local/schema/v1/site.json");
    }

    [Fact]
    public async Task PreviewRoot_ReturnsDefaultThemeHomeAfterFullBuild()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var build = await client.PostAsync("/Admin/Publish/run", content: null);
        build.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Selected Writing");
        html.Should().Contain("bocchi-preview-toolbar");
    }

    [Fact]
    public async Task PreviewMediaEndpoint_ReturnsMediaBytes()
    {
        var mediaPath = _factory.SeedPublishedPostWithMedia();
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/_bocchi/preview" + mediaPath);

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith([0xFF, 0xD8]);
    }

    [Fact]
    public async Task PreviewEndpoint_RejectsPathTraversal()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/_bocchi/preview/media/%2e%2e/secret.jpg");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
