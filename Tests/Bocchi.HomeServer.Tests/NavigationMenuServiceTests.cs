using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;
using Bocchi.Workspace.Scanning;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

public sealed class NavigationMenuServiceTests
{
    [Fact]
    public async Task SaveAsync_WritesMenuAndPreservesCommonI18nOverrides()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var localization = scope.ServiceProvider.GetRequiredService<LocalizationSettingsService>();
        await localization.SaveAsync("en-US", ["zh-CN"], []);
        await localization.SaveCommonTextOverridesAsync(
        [
            new CommonI18nTextOverride
            {
                Key = "content.translationNotice",
                Values = new Dictionary<string, string>
                {
                    ["en-US"] = "Reading a translation",
                },
            },
        ]);

        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        await menu.SaveAsync(
            [
                new NavigationEditorItem
                {
                    Id = "custom",
                    Label = "i18n://common@menu.custom.docs",
                    TargetType = "builtin",
                    TargetValue = "home",
                },
                new NavigationEditorItem
                {
                    Id = "theme-ref",
                    Label = "i18n://theme@theme.bocchi-mono.colophonBuiltWith",
                    TargetType = "builtin",
                    TargetValue = "posts",
                },
            ],
            [
                new CommonI18nTextOverride
                {
                    Key = "content.translationNotice",
                    Values = new Dictionary<string, string>
                    {
                        ["en-US"] = "Reading a translation",
                    },
                },
                new CommonI18nTextOverride
                {
                    Key = "menu.custom.docs",
                    Values = new Dictionary<string, string>
                    {
                        ["en-US"] = "Docs",
                        ["zh-CN"] = "文档",
                    },
                },
            ]);

        var layout = scope.ServiceProvider.GetRequiredService<BocchiDataLayout>();
        var yaml = await File.ReadAllTextAsync(layout.Workspace.NavigationFile);
        yaml.Should().Contain("label: i18n://common@menu.custom.docs");
        yaml.Should().Contain("label: i18n://theme@theme.bocchi-mono.colophonBuiltWith");

        var settings = await localization.GetBuildLocalizationOptionsAsync();
        settings.Text["menu.custom.docs"]["en-US"].Should().Be("Docs");
        settings.Text["menu.custom.docs"]["zh-CN"].Should().Be("文档");
        settings.Text["content.translationNotice"]["en-US"].Should().Be("Reading a translation");

        var view = await menu.GetEditorAsync();
        view.EnabledLanguages.Select(language => language.Code).Should().Contain(["en-US", "zh-CN"]);
        view.CommonTextOverrides.Should().Contain(overrideText => overrideText.Key == "menu.custom.docs");
        view.TargetOptions.Select(option => option.GroupLabel).Should().Contain("Built-in");
        view.Items.Should().Contain(item => item.Label == "i18n://theme@theme.bocchi-mono.colophonBuiltWith");
    }

    [Fact]
    public async Task SaveAsync_PreservesNoTargetItemsWithoutWarnings()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        await menu.SaveAsync(
        [
            new NavigationEditorItem
            {
                Id = "about",
                Label = "i18n://common@menu.about",
            },
        ]);

        var layout = scope.ServiceProvider.GetRequiredService<BocchiDataLayout>();
        var yaml = await File.ReadAllTextAsync(layout.Workspace.NavigationFile);
        yaml.Should().Contain("id: about");
        yaml.Should().NotContain("type: ''");

        var view = await menu.GetEditorAsync();
        view.Items.Should().ContainSingle(item => item.Id == "about" && !item.HasTarget);
        view.Warnings.Should().BeEmpty();
        view.TargetOptions.Should().Contain(option => option.Key == NavigationTargetOption.CreateKey(string.Empty, string.Empty));
    }

    [Fact]
    public async Task ApplyDefaultPresetAsync_UsesNoTargetAboutWhenPageIsMissing()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        await menu.SaveAsync([]);

        var applied = await menu.ApplyDefaultPresetAsync();

        applied.Should().BeTrue();
        var view = await menu.GetEditorAsync();
        view.Items.Select(item => item.Id).Should().Equal("home", "posts", "notes", "works", "about");
        var about = view.Items.Single(item => item.Id == "about");
        about.HasTarget.Should().BeFalse();
        about.Label.Should().Be("i18n://common@menu.about");
    }

    [Fact]
    public async Task GetEditorAsync_PreservesUnavailablePageTargetsAsWarnings()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        await menu.SaveAsync(
        [
            new NavigationEditorItem
            {
                Id = "missing-about",
                Label = "Missing About",
                TargetType = "page",
                TargetValue = "about",
            },
        ]);

        var view = await menu.GetEditorAsync();

        view.Items.Single().TargetType.Should().Be("page");
        view.Warnings.Should().ContainSingle(warning => warning.ItemId == "missing-about");
        view.TargetOptions.Should().Contain(option => option.Key == NavigationTargetOption.CreateKey("page", "about") && !option.Available);
    }

    [Fact]
    public async Task ApplyDefaultPresetAsync_BindsAboutLikePageWhenAvailable()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        var editor = scope.ServiceProvider.GetRequiredService<ContentEditingService>();
        await editor.CreateFromDraftAsync(
            ContentKind.Page,
            """
            title: About Me
            slug: aboutme
            status: published
            template: normal
            order: 0
            showInNavigation: false
            """,
            string.Empty,
            sourceAssetsDirectory: null);
        await scope.ServiceProvider.GetRequiredService<ContentScanner>().ScanAsync();

        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        await menu.SaveAsync([]);

        var applied = await menu.ApplyDefaultPresetAsync();

        applied.Should().BeTrue();
        var about = (await menu.GetEditorAsync()).Items.Single(item => item.Id == "about");
        about.TargetType.Should().Be("page");
        about.TargetValue.Should().Be("aboutme");
    }
}
