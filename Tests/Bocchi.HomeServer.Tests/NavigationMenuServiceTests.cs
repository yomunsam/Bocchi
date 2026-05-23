using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

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
                    Label = "i18n://theme@theme.defaultStatic.colophonBuiltWith",
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
        yaml.Should().Contain("label: i18n://theme@theme.defaultStatic.colophonBuiltWith");

        var settings = await localization.GetBuildLocalizationOptionsAsync();
        settings.Text["menu.custom.docs"]["en-US"].Should().Be("Docs");
        settings.Text["menu.custom.docs"]["zh-CN"].Should().Be("文档");
        settings.Text["content.translationNotice"]["en-US"].Should().Be("Reading a translation");

        var view = await menu.GetEditorAsync();
        view.EnabledLanguages.Select(language => language.Code).Should().Contain(["en-US", "zh-CN"]);
        view.CommonTextOverrides.Should().Contain(overrideText => overrideText.Key == "menu.custom.docs");
        view.TargetOptions.Select(option => option.GroupLabel).Should().Contain("Built-in");
        view.Items.Should().Contain(item => item.Label == "i18n://theme@theme.defaultStatic.colophonBuiltWith");
    }
}
