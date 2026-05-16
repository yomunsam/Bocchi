using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

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
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Good to see you.");
        body.Should().Contain("bocchi-content-feed");
        body.Should().Contain("Site preview");
        body.Should().Contain("/healthz");
    }

    [Fact]
    public async Task WorkspacePage_RendersAndShowsConfiguredRoot()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Content");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("工作区");
        body.Should().Contain(_factory.WorkspaceRoot);
    }

    [Fact]
    public async Task AdminRoute_RedirectsToSetupBeforeInitialization()
    {
        using var factory = new IsolatedWorkspaceWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Setup");
    }

    [Fact]
    public async Task AdminRoute_RedirectsToLoginAfterSetupWhenAnonymous()
    {
        using var factory = new IsolatedWorkspaceWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await anonymous.GetAsync("/Admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Account/Login");
    }

    [Fact]
    public async Task ProtectedPreview_RendersToolbarForAuthenticatedUser()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("bocchi-preview-toolbar");
        body.Should().Contain("Preview");
    }

    [Fact]
    public async Task SettingsPage_RendersThemeAndExternalLoginSections()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Settings");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Third-party Login");
        body.Should().Contain("Localization");
        body.Should().Contain("Site primary language");
        body.Should().Contain("PrimaryUnprefixed");
        body.Should().Contain("Save Theme config");
    }

    [Fact]
    public async Task DashboardUiLanguageEndpoint_SetsCultureCookieAndRendersChineseSettings()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.PostAsync("/Admin/Settings/Localization/UiLanguage", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["uiLanguage"] = "zh-CN",
            ["returnUrl"] = "/Admin/Settings#localization",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Admin/Settings#localization");

        var settings = await client.GetAsync("/Admin/Settings");
        settings.EnsureSuccessStatusCode();
        var body = await settings.Content.ReadAsStringAsync();
        body.Should().Contain("本地化");
        body.Should().Contain("保持服务器清爽");
        body.Should().Contain("站点主要语言");
    }

    [Fact]
    public async Task UsersPage_RendersRoleAndExternalLoginManagement()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Users");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Remove Admin");
        body.Should().Contain("No external login bound.");
    }

    [Fact]
    public async Task EditorPage_RendersDiffConfirmationForContentFile()
    {
        _factory.SeedPublishedPostWithMedia();
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Content/Edit?path=posts%2F2026%2Fhello-preview%2Findex.md");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Diff before save");
        body.Should().Contain("I reviewed these changes");
    }
}
