using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

/// <summary>
/// 验证 ThemeMigrationService 扫描 Menu 中 Theme i18n 引用并按决策改写。
/// </summary>
public sealed class ThemeMigrationServiceTests
{
    [Fact]
    public async Task ScanAsync_FindsThemeRefsAndIncludesOverrideValues()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        await SeedAsync(scope);

        var migration = scope.ServiceProvider.GetRequiredService<ThemeMigrationService>();
        var plan = await migration.ScanAsync("bocchi-mono", "some-other-theme");

        plan.Entries.Should().HaveCount(1);
        var entry = plan.Entries[0];
        entry.OldKey.Should().Be("theme.bocchi-mono.colophonBuiltWith");
        entry.OldValues["en-US"].Should().Be("Built with Bocchi (custom)");
        entry.OldValues["zh-CN"].Should().Be("基于 Bocchi（自定义）");
        // 目标 Theme 没有 manifest，因此不应识别为已存在
        entry.ExistsInNewTheme.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_ConvertsToCommonI18nAndSwitchesTheme()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        await SeedAsync(scope);

        var migration = scope.ServiceProvider.GetRequiredService<ThemeMigrationService>();
        var plan = await migration.ScanAsync("bocchi-mono", "some-other-theme");
        var entry = plan.Entries.Single();

        var decisions = new Dictionary<string, ThemeMigrationDecision>(StringComparer.Ordinal)
        {
            [entry.ItemId] = new ThemeMigrationDecision.ToCommonI18n("menu.custom.colophon"),
        };
        await migration.ApplyAsync(plan, decisions);

        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        var view = await menu.GetEditorAsync();
        view.Items.Single(item => item.Id == entry.ItemId).Label
            .Should().Be("i18n://common@menu.custom.colophon");

        var commonOverride = view.CommonTextOverrides
            .Single(x => x.Key == "menu.custom.colophon");
        commonOverride.Values["en-US"].Should().Be("Built with Bocchi (custom)");
        commonOverride.Values["zh-CN"].Should().Be("基于 Bocchi（自定义）");

        var site = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        (await site.GetAsync()).DefaultThemeId.Should().Be("some-other-theme");
    }

    [Fact]
    public async Task ApplyAsync_ToPlainText_FlattensLabelToPrimaryLanguage()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        using var scope = factory.Services.CreateScope();
        await SeedAsync(scope);

        var migration = scope.ServiceProvider.GetRequiredService<ThemeMigrationService>();
        var plan = await migration.ScanAsync("bocchi-mono", "some-other-theme");
        var entry = plan.Entries.Single();

        await migration.ApplyAsync(plan, new Dictionary<string, ThemeMigrationDecision>
        {
            [entry.ItemId] = new ThemeMigrationDecision.ToPlainText("en-US"),
        });

        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        var view = await menu.GetEditorAsync();
        view.Items.Single(item => item.Id == entry.ItemId).Label
            .Should().Be("Built with Bocchi (custom)");
    }

    /// <summary>启用 en-US/zh-CN 并写入一个含 Theme i18n 覆盖值的 Menu。</summary>
    private static async Task SeedAsync(IServiceScope scope)
    {
        var localization = scope.ServiceProvider.GetRequiredService<LocalizationSettingsService>();
        await localization.SaveAsync("en-US", ["zh-CN"], []);

        var theme = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();
        await theme.SaveI18nTextOverridesAsync("bocchi-mono",
        [
            new ThemeI18nTextOverride
            {
                Key = "theme.bocchi-mono.colophonBuiltWith",
                Values = new Dictionary<string, string>
                {
                    ["en-US"] = "Built with Bocchi (custom)",
                    ["zh-CN"] = "基于 Bocchi（自定义）",
                },
            },
        ]);

        var menu = scope.ServiceProvider.GetRequiredService<NavigationMenuService>();
        await menu.SaveAsync(
        [
            new NavigationEditorItem
            {
                Id = "theme-ref",
                Label = "i18n://theme@theme.bocchi-mono.colophonBuiltWith",
                TargetType = "builtin",
                TargetValue = "home",
            },
        ]);
    }
}
