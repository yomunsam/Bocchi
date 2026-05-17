using System.Net;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

public sealed class AccountAndSetupTests
{
    [Fact]
    public async Task SetupPost_CreatesFirstAdminAndClosesSetup()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = await factory.CreateAdminClientAsync();

        var admin = await client.GetAsync("/Admin");
        var setup = await client.GetAsync("/Setup");

        admin.EnsureSuccessStatusCode();
        setup.StatusCode.Should().Be(HttpStatusCode.Redirect);
        setup.Headers.Location!.ToString().Should().Be("/Admin");
    }

    [Fact]
    public async Task NonAdminUser_CannotEnterDashboard()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        await factory.CreateLocalUserAsync("reader@example.test", "reader-password", isAdmin: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "reader@example.test",
            ["password"] = "reader-password",
        }));
        var admin = await client.GetAsync("/Admin");

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        admin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        admin.Headers.Location!.ToString().Should().Contain("/Account/Denied");
    }

    [Fact]
    public async Task ExternalLoginSettings_ProtectSecretAndControlLoginButtons()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        using (var scope = factory.Services.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<ExternalLoginSettingsService>();
            await settings.SaveGitHubAsync(
                enabled: true,
                displayName: "GitHub",
                clientId: "github-client",
                clientSecret: "github-secret",
                callbackPath: "/signin-github");

            var db = scope.ServiceProvider.GetRequiredService<BocchiDbContext>();
            var provider = db.ExternalLoginProviders.Single(x => x.ProviderKey == "github");
            provider.ProtectedClientSecret.Should().NotBeNullOrWhiteSpace();
            provider.ProtectedClientSecret.Should().NotBe("github-secret");
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.GetAsync("/Account/Login");

        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadAsStringAsync();
        body.Should().Contain("Continue with GitHub");
    }

    [Fact]
    public async Task ThemeSettings_SaveDefault_SyncsWorkspaceConfigFile()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();
        await settings.SaveDefaultAsync("default-static", """{"visual":{"accentColor":"#E85D3A"}}""");

        var db = scope.ServiceProvider.GetRequiredService<BocchiDbContext>();
        db.ThemeConfigurations.Single().ThemeId.Should().Be("default-static");
        var layout = scope.ServiceProvider.GetRequiredService<BocchiDataLayout>();
        var configPath = Path.Combine(layout.ThemeConfigDirectory, "default-static.json");
        File.Exists(configPath).Should().BeTrue();
        (await File.ReadAllTextAsync(configPath)).Should().Contain("#E85D3A");
    }

    [Fact]
    public async Task ThemeSettings_SaveI18nTextOverrides_NormalizesAndExportsBuildSnapshot()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();
        var view = await settings.GetI18nAsync("default-static");
        view.Keys.Should().ContainSingle(key => key.Key == "theme.defaultStatic.colophonBuiltWith");

        await settings.SaveI18nTextOverridesAsync(
            "default-static",
            [
                new ThemeI18nTextOverride
                {
                    Key = " theme.defaultStatic.colophonBuiltWith ",
                    Values = new Dictionary<string, string>
                    {
                        [" en-US "] = " Powered quietly ",
                        ["zh-CN"] = " ",
                    },
                },
            ]);

        var updated = await settings.GetI18nAsync("default-static");
        updated.TextOverrides.Should().ContainSingle(x =>
            x.Key == "theme.defaultStatic.colophonBuiltWith"
            && x.Values["en-US"] == "Powered quietly");
        var snapshot = await settings.GetBuildI18nTextOverridesAsync("default-static");
        snapshot["theme.defaultStatic.colophonBuiltWith"]["en-US"].Should().Be("Powered quietly");
    }
}
