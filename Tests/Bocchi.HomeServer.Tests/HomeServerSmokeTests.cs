using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;

namespace Bocchi.HomeServer.Tests;

public sealed class HomeServerSmokeTests : IClassFixture<IsolatedDataRootWebApplicationFactory>
{
    private readonly IsolatedDataRootWebApplicationFactory _factory;

    public HomeServerSmokeTests(IsolatedDataRootWebApplicationFactory factory)
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
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        // 新版首页的关键骨架：页头 + 快捷动作 + composer + 预览卡 + 最近更新。
        body.Should().Contain("bocchi-page-intro");
        body.Should().Contain("bocchi-quick-actions");
        body.Should().Contain("bocchi-composer");
        body.Should().Contain("bocchi-preview-card");
        body.Should().Contain("bocchi-recent");
        body.Should().Contain("bocchi-menu-control--appearance");
        body.Should().Contain("data-bocchi-appearance-option=\"auto\"");
        body.Should().NotContain("bocchi-appearance-select");
        body.Should().NotContain("Ctrl K");
        body.Should().Contain("Open the editor and shape a longer piece.");
        body.Should().NotContain("No fact-checking required.");
        body.Should().NotContain("Good to see you.");
        body.Should().NotContain("Setup complete");
    }

    [Fact]
    public async Task WorkspacePage_RendersAndShowsConfiguredRoot()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Content");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("工作区");
        body.Should().Contain(_factory.DataRoot);
    }

    [Fact]
    public async Task AdminRoute_RedirectsToSetupBeforeInitialization()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Setup");
    }

    [Fact]
    public async Task AdminRoute_RedirectsToLoginAfterSetupWhenAnonymous()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
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
    public async Task SettingsPage_RendersLocalizationAndExternalLoginSections()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Settings");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Third-party Login");
        body.Should().Contain("Localization");
        body.Should().Contain("Site primary language");
        body.Should().Contain("Common theme text");
        body.Should().Contain("menu.home");
        body.Should().Contain("PrimaryUnprefixed");
        body.Should().NotContain("Save Theme config");
    }

    [Fact]
    public async Task SiteNavigationPage_RendersPlaceholderAndSiteSidebarLinks()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Site/Navigation");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("Entry ready");
        body.Should().Contain("Navigation");
        body.Should().Contain("Theme customization");
    }

    [Fact]
    public async Task ThemeCustomizationPage_RendersSchemaEditorAndThemePrivateText()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Site/Theme");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("Theme customization");
        body.Should().Contain("Bocchi Mono");
        body.Should().Contain("visual.accentColor");
        body.Should().Contain("Save settings");
        body.Should().Contain("Theme private text");
        body.Should().Contain("theme.defaultStatic.colophonBuiltWith");
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
        // Blazor prerender 会把非 ASCII 文案编码成 HTML entity；这里按浏览器看到的文本断言真实渲染结果。
        var body = WebUtility.HtmlDecode(await settings.Content.ReadAsStringAsync());
        body.Should().Contain("本地化");
        body.Should().Contain("保持服务器清爽");
        body.Should().Contain("站点主要语言");

        var home = await client.GetAsync("/Admin");
        home.EnsureSuccessStatusCode();
        body = WebUtility.HtmlDecode(await home.Content.ReadAsStringAsync());
        // 新版首页改用 Dashboard 主标题与站点预览卡作为中文渲染锚点。
        body.Should().Contain("下午好");
        body.Should().Contain("站点预览");
        body.Should().Contain("打开编辑器，慢慢写一篇长文。");
        body.Should().NotContain("没有事实核对。");
        body.Should().NotContain("Setup 已完成");
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
