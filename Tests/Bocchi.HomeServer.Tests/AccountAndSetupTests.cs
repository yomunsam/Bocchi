using System.Net;
using System.Text.Json;

using Bocchi.GeneratorContract;
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

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BocchiUser>>();
        var user = await users.FindByNameAsync(IsolatedDataRootWebApplicationFactory.AdminUserName);
        user.Should().NotBeNull();
        user!.Email.Should().BeNull();
    }

    [Fact]
    public async Task SetupPost_SavesSiteProfileAndWorkspaceProjection()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BocchiDbContext>();
        var site = db.SiteProfileSettings.Single();
        site.SiteName.Should().Be("Bocchi Test Site");
        site.DefaultTitle.Should().Be("Bocchi Test");
        site.PublicBaseUrl.Should().Be("https://bocchi.example/");
        site.CopyrightNotice.Should().Be("Copyright © 2026 Bocchi Test.");

        var layout = scope.ServiceProvider.GetRequiredService<BocchiDataLayout>();
        var yaml = await File.ReadAllTextAsync(layout.Workspace.SiteSettingsFile);
        yaml.Should().Contain("title: Bocchi Test Site");
        yaml.Should().Contain("defaultTitle: Bocchi Test");
        yaml.Should().Contain("baseUrl: https://bocchi.example/");
        yaml.Should().Contain("copyright: Copyright © 2026 Bocchi Test.");
    }

    [Fact]
    public async Task SetupFlow_RendersAdminAndSiteBasicsAsSeparatePages()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var adminStep = await client.GetAsync("/Setup");
        adminStep.EnsureSuccessStatusCode();
        var adminBody = await adminStep.Content.ReadAsStringAsync();
        adminBody.Should().Contain("name=\"username\"");
        adminBody.Should().Contain("name=\"email\"");
        adminBody.Should().Contain("<link rel=\"icon\" href=\"/favicon.ico\" sizes=\"any\">");
        adminBody.Should().Contain("<link rel=\"apple-touch-icon\" sizes=\"180x180\" href=\"/apple-touch-icon.png\">");
        adminBody.Should().Contain("class=\"bocchi-password-meter\" data-strength=\"weak\"");
        adminBody.Should().Contain("data-ok=\"false\" data-bocchi-password-rule=\"length\"");
        adminBody.Should().Contain("data-ok=\"false\" data-bocchi-password-rule=\"match\"");
        adminBody.Should().NotContain("name=\"siteName\"");

        var siteStep = await client.PostAsync("/Setup/Site", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["displayName"] = "Bocchi Admin",
            ["email"] = string.Empty,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        siteStep.EnsureSuccessStatusCode();
        var siteBody = await siteStep.Content.ReadAsStringAsync();
        siteBody.Should().Contain("name=\"setupPayload\"");
        siteBody.Should().Contain("name=\"siteName\"");
        siteBody.Should().Contain("<select name=\"defaultThemeId\"");
        siteBody.Should().Contain("placeholder=\"https://domain.com/\"");
        siteBody.Should().NotContain("name=\"publicBaseUrl\" type=\"url\" value=\"\" placeholder=\"https://domain.com/\" required");
        siteBody.Should().Contain("name=\"description\" type=\"text\" value=\"\"");
        siteBody.Should().Contain("Finish initialization");
        siteBody.Should().NotContain("workspace/site/site.yaml");
        siteBody.Should().NotContain("http://127.0.0.1");
        siteBody.Should().NotContain("name=\"password\"");
    }

    [Fact]
    public async Task SetupGate_AllowsDefaultIconAssetsBeforeSetup()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var favicon = await client.GetAsync("/favicon.ico");
        var manifest = await client.GetAsync("/site.webmanifest");
        var webAppIcon = await client.GetAsync("/icons/bocchi-icon-192.png");

        favicon.EnsureSuccessStatusCode();
        manifest.EnsureSuccessStatusCode();
        webAppIcon.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SetupComplete_AllowsBlankPublicBaseUrl()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var siteStep = await client.PostAsync("/Setup/Site", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["displayName"] = "Bocchi Admin",
            ["email"] = string.Empty,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        siteStep.EnsureSuccessStatusCode();
        var siteBody = await siteStep.Content.ReadAsStringAsync();
        var setupPayload = ExtractHiddenFieldValue(siteBody, "setupPayload");

        var completed = await client.PostAsync("/Setup/Complete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["setupPayload"] = setupPayload,
            ["siteName"] = "Blank URL Site",
            ["defaultTitle"] = "Blank URL Site",
            ["description"] = string.Empty,
            ["publicBaseUrl"] = string.Empty,
            ["copyrightNotice"] = "Copyright © 2026 Blank URL Site.",
            ["defaultThemeId"] = "default-static",
        }));

        completed.StatusCode.Should().Be(HttpStatusCode.Redirect);
        completed.Headers.Location!.ToString().Should().Be("/Admin");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BocchiDbContext>();
        db.SiteProfileSettings.Single().PublicBaseUrl.Should().BeEmpty();
    }

    [Fact]
    public async Task NonAdminUser_CannotEnterDashboard()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        await factory.CreateLocalUserAsync("reader", "reader-password", isAdmin: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "reader",
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
    public async Task ThemeSettings_SaveCustomization_PreservesUnknownJsonAndWritesTypedValues()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();
        await settings.SaveDefaultAsync("default-static", """{"manual":{"keep":"yes"},"visual":{"accentColor":"#111111"}}""");

        await settings.SaveCustomizationAsync(
            "default-static",
            [
                new ThemeConfigValueInput
                {
                    Key = "visual.accentColor",
                    Value = "#123456",
                },
                new ThemeConfigValueInput
                {
                    Key = "home.featuredPosts",
                    Value = "7",
                },
                new ThemeConfigValueInput
                {
                    Key = "home.heroTitle",
                    LocalizedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["zh-CN"] = "  自定义首页标题  ",
                        ["en-US"] = " ",
                    },
                },
                new ThemeConfigValueInput
                {
                    Key = "home.tags",
                    LocalizedListValues = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["zh-CN"] = ["设计", "构建", "设计", " "],
                        ["en-US"] = [],
                    },
                },
            ]);

        var layout = scope.ServiceProvider.GetRequiredService<BocchiDataLayout>();
        var configPath = Path.Combine(layout.ThemeConfigDirectory, "default-static.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var root = document.RootElement;
        root.GetProperty("manual").GetProperty("keep").GetString().Should().Be("yes");
        root.GetProperty("visual").GetProperty("accentColor").GetString().Should().Be("#123456");
        root.GetProperty("home").GetProperty("featuredPosts").GetDecimal().Should().Be(7);
        root.GetProperty("home").GetProperty("heroTitle").GetProperty("zh-CN").GetString().Should().Be("自定义首页标题");
        root.GetProperty("home").GetProperty("heroTitle").TryGetProperty("en-US", out _).Should().BeFalse();
        root.GetProperty("home").GetProperty("tags").GetProperty("zh-CN").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("设计", "构建");
    }

    [Fact]
    public async Task ThemeSettings_GetCustomization_ExposesLocalizedHomeFieldsWithDefaults()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();

        var view = await settings.GetCustomizationAsync("default-static");

        var homeFields = view.Groups.Single(group => group.Id == "home").Fields;
        var heroTitle = homeFields.Single(field => field.Key == "home.heroTitle");
        heroTitle.Type.Should().Be(ThemeConfigFieldType.LocalizedText);
        heroTitle.LocalizedTextValues.Should().BeEmpty();
        heroTitle.DefaultLocalizedTextValues["zh-CN"].Should().Be("Bocchi — 写作、\n作品与札记。");

        var tags = homeFields.Single(field => field.Key == "home.tags");
        tags.Type.Should().Be(ThemeConfigFieldType.LocalizedTextList);
        tags.DefaultLocalizedTextListValues["zh-CN"].Should().Equal("个人站点", "软件与文字", "静态优先");
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

    private static string ExtractHiddenFieldValue(string html, string fieldName)
    {
        var nameNeedle = $"name=\"{fieldName}\"";
        var nameIndex = html.IndexOf(nameNeedle, StringComparison.Ordinal);
        nameIndex.Should().BeGreaterThanOrEqualTo(0, $"Setup step 2 should include hidden field {fieldName}.");

        var valueNeedle = "value=\"";
        var valueIndex = html.IndexOf(valueNeedle, nameIndex, StringComparison.Ordinal);
        valueIndex.Should().BeGreaterThanOrEqualTo(0, $"Hidden field {fieldName} should include a value.");
        var valueStart = valueIndex + valueNeedle.Length;
        var valueEnd = html.IndexOf('"', valueStart);
        valueEnd.Should().BeGreaterThan(valueStart, $"Hidden field {fieldName} should not be empty.");
        return WebUtility.HtmlDecode(html[valueStart..valueEnd]);
    }
}
