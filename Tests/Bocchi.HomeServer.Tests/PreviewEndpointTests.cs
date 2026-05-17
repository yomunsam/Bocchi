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
    public async Task PreviewRoot_ReturnsDefaultThemeHomeWithoutFullBuild()
    {
        using var client = await _factory.CreateAdminClientAsync();
        var publicIndex = Path.Combine(_factory.DataRoot, "output", "public", "index.html");
        File.Exists(publicIndex).Should().BeFalse();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Selected Writing");
        html.Should().Contain("bocchi-preview-toolbar");
        File.Exists(publicIndex).Should().BeFalse("实时预览不能物化静态发布目录");
    }

    [Fact]
    public async Task PreviewThemeAsset_ReturnsDefaultThemeAssetWithoutFullBuild()
    {
        using var client = await _factory.CreateAdminClientAsync();
        var publicAsset = Path.Combine(_factory.DataRoot, "output", "public", "assets", "app.css");
        File.Exists(publicAsset).Should().BeFalse();

        var response = await client.GetAsync("/assets/app.css");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/css");
        var css = await response.Content.ReadAsStringAsync();
        css.Should().Contain(".topbar");
        File.Exists(publicAsset).Should().BeFalse("实时预览资源不能写入静态发布目录");
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
