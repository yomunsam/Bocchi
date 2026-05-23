using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace.State;

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
    public async Task ContentEditingService_CreatesPostAndPageDrafts()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();

        var post = await editor.CreateDraftAsync(ContentKind.Post);
        var page = await editor.CreateDraftAsync(ContentKind.Page);

        post.RelativePath.Should().StartWith("posts/");
        post.RelativePath.Should().EndWith("/index.md");
        post.Yaml.Should().Contain("status: draft");
        post.Markdown.Should().Contain("# New Post");
        File.Exists(Path.Combine(factory.DataRoot, "workspace", post.RelativePath)).Should().BeTrue();

        page.RelativePath.Should().Be("pages/new-page/index.md");
        page.Yaml.Should().Contain("template: normal");
        File.Exists(Path.Combine(factory.DataRoot, "workspace", page.RelativePath)).Should().BeTrue();
    }

    [Fact]
    public async Task ContentEditingService_ValidatesUnicodeSlugAndDuplicateScope()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();
        var post = await editor.CreateDraftAsync(ContentKind.Post);
        var page = await editor.CreateDraftAsync(ContentKind.Page);

        var normalized = editor.ValidateUrlSlug(ContentKind.Post, post.RelativePath, " 我的 第一篇文章！");
        normalized.IsAvailable.Should().BeTrue();
        normalized.Slug.Should().Be("我的-第一篇文章");

        await editor.SaveAsync(
            post.RelativePath,
            "title: 我的第一篇文章\nslug: 我的-第一篇文章\nstatus: draft",
            post.Markdown);
        var otherPost = await editor.CreateDraftAsync(ContentKind.Post);

        editor.ValidateUrlSlug(ContentKind.Post, otherPost.RelativePath, "我的 第一篇文章")
            .IsAvailable.Should().BeFalse();
        editor.ValidateUrlSlug(ContentKind.Post, post.RelativePath, "我的 第一篇文章")
            .IsAvailable.Should().BeTrue();
        editor.ValidateUrlSlug(ContentKind.Page, page.RelativePath, "posts")
            .IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task ContentEditor_CreatePostDraftRendersMarkdownEditor()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = await factory.CreateAdminClientAsync();
        using (var categoryScope = factory.Services.CreateScope())
        {
            var categories = categoryScope.ServiceProvider.GetRequiredService<CategoryTreeService>();
            await categories.SaveAsync(
                ContentKind.Post,
                [new CategoryTreeNode("design", "Design", "design", [])]);
        }

        var response = await client.GetAsync("/Admin/Content/Edit?kind=post");
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            response = await client.GetAsync(response.Headers.Location!.ToString());
        }

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("写作与发布");
        body.Should().Contain("bocchi-markdown-editor");
        body.Should().Contain("bocchi-codemirror-host");
        body.Should().Contain("data-bocchi-codemirror-host");
        body.Should().Contain("posts/");
        body.Should().Contain("Frontmatter");
        body.Should().Contain("发布状态");
        body.Should().Contain("内容设置");
        body.Should().Contain("AI助手");
        body.Should().Contain("value=\"Design\"");
        body.Should().Contain(">Design</option>");
        body.Should().NotContain("选择要编辑的 Markdown");

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IContentStateStore>();
        var posts = await store.ListContentSummariesAsync(ContentKind.Post);
        posts.Should().ContainSingle(x => x.RelativePath.StartsWith("posts/", StringComparison.Ordinal));
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
    public async Task SiteNavigationPage_RendersEditorAndSiteSidebarLinks()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using var client = await factory.CreateAdminClientAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
            await menu.SaveAsync(
            [
                new NavigationEditorItem
                {
                    Id = "home",
                    TargetType = "builtin",
                    TargetValue = "home",
                },
            ]);
        }

        var response = await client.GetAsync("/Admin/Site/Navigation");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("Menu tree");
        body.Should().Contain("Menu item details");
        body.Should().Contain("Label mode");
        body.Should().Contain("Site i18n");
        body.Should().Contain("Add root item");
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
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("bocchi-markdown-editor");
        body.Should().Contain("data-bocchi-codemirror-host");
        body.Should().Contain("Frontmatter");
        body.Should().Contain("发布状态");
        body.Should().Contain("内容设置");
        body.Should().Contain("AI助手");
    }
}
