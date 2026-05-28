using System.Net;
using System.Security.Claims;
using System.Text.Json;

using Bocchi.GeneratorContract;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Security;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

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
        setup.Headers.Location!.ToString().Should().EndWith("/Admin");

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
        adminBody.Should().Contain("action=\"/Setup/UiLanguage\"");
        adminBody.Should().Contain("rel=\"icon\"");
        adminBody.Should().Contain("favicon");
        adminBody.Should().Contain("rel=\"apple-touch-icon\"");
        adminBody.Should().Contain("apple-touch-icon");
        adminBody.Should().Contain("bocchi-password-meter");
        adminBody.Should().Contain("data-strength=\"weak\"");
        adminBody.Should().Contain("data-ok=\"false\" data-bocchi-password-rule=\"length\"");
        adminBody.Should().Contain("data-ok=\"false\" data-bocchi-password-rule=\"match\"");
        adminBody.Should().NotContain("name=\"siteName\"");

        var siteStep = await client.PostAsync("/Setup/Admin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["displayName"] = "Bocchi Admin",
            ["email"] = string.Empty,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        siteStep.StatusCode.Should().Be(HttpStatusCode.Redirect);
        siteStep.Headers.Location!.ToString().Should().Be("/Setup/Site");

        var sitePage = await client.GetAsync("/Setup/Site");
        sitePage.EnsureSuccessStatusCode();
        var siteBody = await sitePage.Content.ReadAsStringAsync();
        siteBody.Should().NotContain("name=\"setupPayload\"");
        siteBody.Should().Contain("name=\"siteName\"");
        siteBody.Should().Contain("name=\"defaultThemeId\"");
        siteBody.Should().Contain("placeholder=\"https://domain.com/\"");
        siteBody.Should().NotContain("name=\"publicBaseUrl\" type=\"url\" value=\"\" placeholder=\"https://domain.com/\" required");
        siteBody.Should().Contain("name=\"description\" type=\"text\" value=\"\"");
        siteBody.Should().Contain("Finish initialization");
        siteBody.Should().NotContain("workspace/site/site.yaml");
        siteBody.Should().NotContain("http://127.0.0.1");
        siteBody.Should().NotContain("name=\"password\"");
    }

    [Fact]
    public async Task SetupPendingAdminCookie_StoresOnlyServerSideHandle()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var siteStep = await client.PostAsync("/Setup/Admin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["displayName"] = "Bocchi Admin",
            ["email"] = string.Empty,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));

        siteStep.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var cookie = SetCookieHeaderValue.ParseList(siteStep.Headers.GetValues(HeaderNames.SetCookie).ToList())
            .Single(x => string.Equals(x.Name.Value, SetupPendingAdminStore.Name, StringComparison.Ordinal));
        var protection = factory.Services.GetRequiredService<IDataProtectionProvider>();
        var payload = protection
            .CreateProtector(SetupPendingAdminStore.ProtectorPurpose)
            .ToTimeLimitedDataProtector()
            .Unprotect(cookie.Value.ToString());

        payload.Should().StartWith("Bocchi.HomeServer.Setup.PendingAdminStore:", "Cookie 只应携带服务端 pending admin 状态句柄");
        payload.Should().NotContain(IsolatedDataRootWebApplicationFactory.AdminPassword);
        payload.Contains("password", StringComparison.OrdinalIgnoreCase)
            .Should().BeFalse("pending admin 密码不应出现在 Cookie payload 中");
    }

    [Fact]
    public async Task AccountPost_RejectsCrossSiteOrigin()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var setupClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var setup = await setupClient.SendAsync(CreateCrossSiteFormPost("/Setup/Admin", new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        setup.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using (await factory.CreateAdminClientAsync())
        {
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.SendAsync(CreateCrossSiteFormPost("/Account/Login/Submit", new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        var external = await client.SendAsync(CreateCrossSiteFormPost("/Account/ExternalLogin", new Dictionary<string, string>
        {
            ["provider"] = "github",
        }));
        var language = await client.SendAsync(CreateCrossSiteFormPost("/Account/UiLanguage", new Dictionary<string, string>
        {
            ["uiLanguage"] = "en-US",
        }));

        login.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        external.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        language.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task LoginPost_LocksOutAfterRepeatedFailures()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        for (var i = 0; i < 5; i++)
        {
            var failed = await client.PostAsync("/Account/Login/Submit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
                ["password"] = "wrong-password",
            }));
            failed.StatusCode.Should().Be(HttpStatusCode.Redirect);
            failed.Headers.Location!.ToString().Should().StartWith("/Account/Login?message=");
        }

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BocchiUser>>();
        var user = await users.FindByNameAsync(IsolatedDataRootWebApplicationFactory.AdminUserName);
        user.Should().NotBeNull();
        (await users.IsLockedOutAsync(user!)).Should().BeTrue();

        var correct = await client.PostAsync("/Account/Login/Submit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        correct.StatusCode.Should().Be(HttpStatusCode.Redirect);
        correct.Headers.Location!.ToString().Should().StartWith("/Account/Login?message=");
    }

    [Fact]
    public async Task LoginPost_UnknownUserUsesGenericInvalidMessage()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsync("/Account/Login/Submit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "missing-user",
            ["password"] = "wrong-password",
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = WebUtility.UrlDecode(response.Headers.Location!.ToString());
        location.Should().Contain("Username or password is incorrect.");
        location.Should().NotContain("disabled");
        location.Should().NotContain("does not exist");
    }

    [Fact]
    public async Task ExternalLoginCallback_WithLinkedLogin_SignsInExistingUser()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        await factory.CreateLocalUserAsync("linked-admin", "linked-admin-password", isAdmin: true, email: "owner@example.test");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<BocchiUser>>();
            var user = await users.FindByNameAsync("linked-admin");
            user.Should().NotBeNull();
            var linked = await users.AddLoginAsync(user!, new UserLoginInfo("github", "remote-owner", "GitHub"));
            linked.Succeeded.Should().BeTrue();
        }

        var externalCookie = CreateExternalLoginCookie(factory, "github", "remote-owner", "owner@example.test");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{externalCookie.Name}={externalCookie.Value}");

        var callback = await client.GetAsync("/Account/ExternalLoginCallback?returnUrl=%2FAdmin");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().Be("/Admin");
    }

    [Fact]
    public async Task ExternalLoginCallback_DoesNotAutoBindByMatchingEmail()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        await factory.CreateLocalUserAsync("email-admin", "email-admin-password", isAdmin: true, email: "owner@example.test");

        var externalCookie = CreateExternalLoginCookie(factory, "github", "remote-owner", "owner@example.test");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{externalCookie.Name}={externalCookie.Value}");

        var callback = await client.GetAsync("/Account/ExternalLoginCallback?returnUrl=%2FAdmin");

        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().StartWith("/Account/Login?message=");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<BocchiUser>>();
        var user = await users.FindByNameAsync("email-admin");
        user.Should().NotBeNull();
        (await users.GetLoginsAsync(user!)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetupGate_RedirectsAccountFlowBeforeSetup()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.GetAsync("/Account/Login");
        var submitLogin = await client.PostAsync("/Account/Login/Submit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.ToString().Should().Be("/Setup");
        submitLogin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        submitLogin.Headers.Location!.ToString().Should().Be("/Setup");
    }

    [Fact]
    public async Task SetupGate_AllowsDefaultIconAssetsBeforeSetup()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var favicon = await client.GetAsync("/favicon.ico");
        var manifest = await client.GetAsync("/site.webmanifest");
        var webAppIcon = await client.GetAsync("/icons/bocchi-icon-192.png");
        var setupPage = await client.GetAsync("/Setup");

        favicon.EnsureSuccessStatusCode();
        manifest.EnsureSuccessStatusCode();
        webAppIcon.EnsureSuccessStatusCode();
        setupPage.EnsureSuccessStatusCode();

        var setupBody = await setupPage.Content.ReadAsStringAsync();
        var appCss = ExtractAssetPath(setupBody, "href=\"app");
        var appearanceJs = ExtractAssetPath(setupBody, "src=\"bocchi-appearance");

        await AssertNotSetupRedirectAsync(client, "/" + appCss);
        await AssertNotSetupRedirectAsync(client, "/" + appearanceJs);
    }

    [Fact]
    public async Task MissingHomeServerAssets_DoNotFallThroughToHtmlPreview()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var missingScriptBeforeSetup = await client.GetAsync("/bocchi-ai.stale.js");
        var missingManifestBeforeSetup = await client.GetAsync("/site.stale.webmanifest");

        missingScriptBeforeSetup.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingManifestBeforeSetup.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingScriptBeforeSetup.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        missingManifestBeforeSetup.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");

        using (await factory.CreateAdminClientAsync())
        {
        }

        var missingFrameworkAfterSetup = await client.GetAsync("/_framework/blazor.web.stale.js");

        missingFrameworkAfterSetup.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingFrameworkAfterSetup.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
    }

    [Fact]
    public async Task SetupComplete_AllowsBlankPublicBaseUrl()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var siteStep = await client.PostAsync("/Setup/Admin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["displayName"] = "Bocchi Admin",
            ["email"] = string.Empty,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        siteStep.StatusCode.Should().Be(HttpStatusCode.Redirect);
        siteStep.Headers.Location!.ToString().Should().Be("/Setup/Site");

        var completed = await client.PostAsync("/Setup/Complete", new FormUrlEncodedContent(new Dictionary<string, string>
        {
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

        var login = await client.PostAsync("/Account/Login/Submit", new FormUrlEncodedContent(new Dictionary<string, string>
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
            var settings = scope.ServiceProvider.GetRequiredService<GitHubIntegrationSettingsService>();
            await settings.SaveAsync(new GitHubIntegrationSettingsUpdate(
                LoginEnabled: true,
                DisplayName: "GitHub",
                OAuthClientId: "github-client",
                OAuthClientSecret: "github-secret",
                CallbackPath: "/signin-github"));

            var db = scope.ServiceProvider.GetRequiredService<BocchiDbContext>();
            var provider = db.GitHubIntegrationSettings.Single();
            provider.ProtectedOAuthClientSecret.Should().NotBeNullOrWhiteSpace();
            provider.ProtectedOAuthClientSecret.Should().NotBe("github-secret");
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.GetAsync("/Account/Login");

        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadAsStringAsync();
        body.Should().Contain("action=\"/Account/UiLanguage\"");
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
                new ThemeConfigValueInput
                {
                    Key = "reading.timeZoneDisplayStyle",
                    Value = "ianaTimeZone",
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
        root.GetProperty("reading").GetProperty("timeZoneDisplayStyle").GetString().Should().Be("ianaTimeZone");

        var invalid = () => settings.SaveCustomizationAsync(
            "default-static",
            [
                new ThemeConfigValueInput
                {
                    Key = "reading.timeZoneDisplayStyle",
                    Value = "windowsTimeZone",
                },
            ]);
        await invalid.Should().ThrowAsync<InvalidOperationException>();
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
        heroTitle.TextFormat.Should().Be("inlineColor");
        heroTitle.LocalizedTextValues.Should().BeEmpty();
        heroTitle.DefaultLocalizedTextValues["zh-CN"].Should().Be("Bocchi — 写作、\n作品[color=accent]与札记。[/color]");
        heroTitle.DefaultText.Should().BeNull();

        var tags = homeFields.Single(field => field.Key == "home.tags");
        tags.Type.Should().Be(ThemeConfigFieldType.LocalizedTextList);
        tags.TextFormat.Should().Be("plain");
        tags.DefaultLocalizedTextListValues["zh-CN"].Should().Equal("个人站点", "软件与文字", "静态优先");
        tags.DefaultText.Should().BeNull();

        var readingFields = view.Groups.Single(group => group.Id == "reading").Fields;
        var timeZoneDisplayStyle = readingFields.Single(field => field.Key == "reading.timeZoneDisplayStyle");
        timeZoneDisplayStyle.Type.Should().Be(ThemeConfigFieldType.Select);
        timeZoneDisplayStyle.TextValue.Should().Be("utcOffset");
        timeZoneDisplayStyle.Options.Select(option => (option.Value, option.Label))
            .Should().Equal(
                ("utcOffset", "UTC offset（UTC+8）"),
                ("ianaTimeZone", "IANA time zone（Asia/Shanghai）"));
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
        var colophonKey = view.Keys.Should().ContainSingle(key => key.Key == "theme.defaultStatic.colophonBuiltWith").Subject;
        colophonKey.DefaultValues["zh-CN"].Should().Be("由 Bocchi 构建。");

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

    /// <summary>验证 Page Contract display ref 会按 Theme override、Theme 默认值和 Common 默认值解析。</summary>
    [Fact]
    public async Task ThemeSettings_GetPageContract_ResolvesThemeAndCommonDisplayRefs()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();
        await settings.SaveI18nTextOverridesAsync(
            "default-static",
            [
                new ThemeI18nTextOverride
                {
                    Key = "theme.defaultStatic.pageTemplate.about",
                    Values = new Dictionary<string, string>
                    {
                        ["zh-CN"] = "关于页面（自定义）",
                    },
                },
            ]);

        var defaultStatic = await settings.GetPageContractAsync("default-static", "zh-CN");
        defaultStatic.PageTemplates.Single(template => template.Name == "normal")
            .DisplayName.Should().Be("普通页面");
        defaultStatic.PageTemplates.Single(template => template.Name == "about")
            .DisplayName.Should().Be("关于页面（自定义）");

        var missingTheme = await settings.GetPageContractAsync("missing-theme", "zh-CN");
        missingTheme.PageTemplates.Should().ContainSingle(template =>
            template.Name == "normal" && template.DisplayName == "普通页面");
    }

    /// <summary>从 prerender HTML 中取出一个已指纹化静态资源路径。</summary>
    private static string ExtractAssetPath(string html, string prefix)
    {
        var start = html.IndexOf(prefix, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start = html.IndexOf('"', start);
        start.Should().BeGreaterThanOrEqualTo(0);
        start++;
        var end = html.IndexOf('"', start + 1);
        end.Should().BeGreaterThan(start);
        return html[start..end];
    }

    /// <summary>验证 Setup gate 不会把指纹化后台静态资源挡回 Setup 页面。</summary>
    private static async Task AssertNotSetupRedirectAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            response.Headers.Location!.ToString().Should().NotBe("/Setup", "asset path {0} should pass through Setup gate", path);
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>构造带跨站浏览器元数据的表单请求，覆盖没有 antiforgery token 的账户端点守卫。</summary>
    private static HttpRequestMessage CreateCrossSiteFormPost(string path, Dictionary<string, string> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.TryAddWithoutValidation(HeaderNames.Origin, "https://evil.example");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        return request;
    }

    /// <summary>构造 Identity external sign-in Cookie，避免测试依赖真实 OAuth provider。</summary>
    private static (string Name, string Value) CreateExternalLoginCookie(
        IsolatedDataRootWebApplicationFactory factory,
        string provider,
        string providerKey,
        string email)
    {
        using var scope = factory.Services.CreateScope();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<BocchiUser>>();
        var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, "/Account/ExternalLoginCallback");
        var identity = new ClaimsIdentity(IdentityConstants.ExternalScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, providerKey));
        identity.AddClaim(new Claim(ClaimTypes.Email, email));
        var principal = new ClaimsPrincipal(identity);
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ExternalScheme);
        var ticket = new AuthenticationTicket(principal, properties, IdentityConstants.ExternalScheme);
        return (options.Cookie.Name!, options.TicketDataFormat.Protect(ticket));
    }

}
