using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Git;
using Bocchi.HomeServer.Services.Publishing;

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
        body.Should().Contain("Ready to publish?");
        body.Should().Contain("Publish now");
        body.Should().Contain("Publish plans");
        body.Should().Contain("No publish plans yet");
        body.Should().NotContain("Publish targets");
        body.Should().NotContain("Current target");
        body.Should().NotContain("Local static output");
        body.Should().NotContain("Advanced options");
        body.Should().NotContain("Frontend Theme id");
        body.Should().NotContain("Build environment");
        body.Should().NotContain("Include drafts");
        body.Should().NotContain("Cloudflare Pages");
        body.Should().NotContain("Local directory");
        body.Should().Contain("Download Zip");
        body.Should().Contain("/Admin/Publish/download");
    }

    [Fact]
    public async Task AddPlanPage_RendersWizardWithoutRawTokenForm()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Publish/AddPlan");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("Publish plan name");
        body.Should().Contain("Publish channel");
        body.Should().Contain("Connect GitHub");
        body.Should().NotContain("Publish repository");
        body.Should().Contain("GitHub cannot connect yet");
        body.Should().Contain("Open GitHub integration");
        body.Should().Contain("/Admin/Settings/Integrations/GitHub");
        body.Should().NotContain("GitHub token");
        body.Should().NotContain("OAuth client id");
    }

    [Fact]
    public async Task AddPlanPage_WithSavedConnection_RendersRepositoryStepAndSearchPicker()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = await factory.CreateAdminClientAsync();
        int planId;
        using (var scope = factory.Services.CreateScope())
        {
            var connections = scope.ServiceProvider.GetRequiredService<GitProviderConnectionService>();
            var credential = new GitHubOAuthCredential
            {
                AccessToken = "test-token",
                Scope = "repo",
                GitHubLogin = "octocat",
            };
            var connection = await connections.SaveAsync(new GitProviderConnectionSaveInput(
                null,
                GitProviderKeys.GitHub,
                "https://github.com",
                "octocat",
                "repo",
                credential.ToJson()));
            var plans = scope.ServiceProvider.GetRequiredService<PublishPlanService>();
            var configuration = new GitHubPagesPublishConfiguration
            {
                Owner = "octocat",
                Repository = "bocchi-site",
                Branch = "gh-pages",
                DestinationMode = "existing",
            };
            var plan = await plans.SaveAsync(new PublishPlanSaveInput(
                null,
                "GitHub Pages",
                PublishPlanService.GitHubPagesChannel,
                configuration.ToJson(),
                CredentialJson: null,
                SetAsDefault: true,
                GitProviderConnectionId: connection.Id));
            planId = plan.Id;
        }

        var response = await client.GetAsync($"/Admin/Publish/AddPlan/{planId}");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("GitHub connected");
        body.Should().Contain("GitHub account");
        body.Should().Contain("Publish repository");
        body.Should().Contain("Search owner/repository");
        body.Should().Contain("octocat/bocchi-site");
        body.Should().NotContain("GitHub cannot connect yet");
        body.Should().NotContain("Open GitHub integration");
        body.Should().NotContain("GitHub token");
    }

    [Fact]
    public async Task GitHubIntegrationPage_RendersSetupGuideAndClientIdForm()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Settings/Integrations/GitHub");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("GitHub integration");
        body.Should().Contain("GitHub OAuth App integration");
        body.Should().Contain("Open GitHub create page");
        body.Should().Contain("View existing OAuth Apps");
        body.Should().Contain("https://github.com/settings/applications/new");
        body.Should().Contain("https://github.com/settings/developers");
        body.Should().Contain("GitHub OAuth App Client ID");
        body.Should().Contain("GitHub OAuth App Client secret");
        body.Should().Contain("View publish settings");
        body.Should().Contain("GitHub / Developer settings / OAuth Apps");
        body.Should().NotContain("GitHub OAuth App configured");
    }

    [Fact]
    public async Task GitHubIntegrationPage_WithSavedClientId_RendersMaintenanceState()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = await factory.CreateAdminClientAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<GitHubIntegrationSettingsService>();
            await settings.SaveAsync(new GitHubIntegrationSettingsUpdate(
                LoginEnabled: false,
                DisplayName: "GitHub",
                OAuthClientId: "saved-client-id",
                OAuthClientSecret: null,
                CallbackPath: "/signin-github"));
        }

        var response = await client.GetAsync("/Admin/Settings/Integrations/GitHub");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("GitHub OAuth App configured");
        body.Should().Contain("Update Bocchi configuration");
        body.Should().Contain("Update GitHub integration");
        body.Should().Contain("View existing OAuth Apps");
        body.Should().Contain("Create another OAuth App");
        body.Should().Contain("value=\"saved-client-id\"");
        body.Should().NotContain("Fill the GitHub form with these values");
        body.Should().NotContain("Check Enable Device Flow");
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
        body.Should().NotContain("GitHub help");
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

    [Fact]
    public async Task GitHubPublishEndpoint_RequiresAdminAuthorization()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var admin = await factory.CreateAdminClientAsync();
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await anonymous.PostAsync("/Admin/Publish/github/run", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
}
