using Microsoft.AspNetCore.Mvc.Testing;

namespace Bocchi.HomeServer.Tests;

public sealed class HomeServerSmokeTests : IClassFixture<IsolatedWorkspaceWebApplicationFactory>
{
    private readonly IsolatedWorkspaceWebApplicationFactory _factory;

    public HomeServerSmokeTests(IsolatedWorkspaceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("Healthy");
    }

    [Fact]
    public async Task RootPage_RendersHomeServerShell()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Bocchi Home Server");
        body.Should().Contain("/healthz");
    }

    [Fact]
    public async Task WorkspacePage_RendersAndShowsConfiguredRoot()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/workspace");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("工作区");
        body.Should().Contain(_factory.WorkspaceRoot);
    }
}