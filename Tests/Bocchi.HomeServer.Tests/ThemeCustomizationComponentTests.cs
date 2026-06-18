using System.Reflection;

using Bocchi.HomeServer.Components.Pages.Admin.Site;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

/// <summary>ThemeCustomization 组件中不经过 HTTP 表单提交的交互逻辑回归测试。</summary>
public sealed class ThemeCustomizationComponentTests
{
    /// <summary>dirty guard 必须先于迁移扫描和配置重载，避免未保存缓冲被静默覆盖。</summary>
    [Fact]
    public async Task SwitchThemeAsync_DirtyEditsBlockThemeChange()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        await WriteThemeAsync(factory.DataRoot, "next-theme");

        using var scope = factory.Services.CreateScope();
        var navigation = new RecordingNavigationManager();
        var component = CreateComponent(scope.ServiceProvider, navigation);
        SetPrivateField(component, "_configDirty", true);

        await InvokeSwitchThemeAsync(component, "next-theme");

        var site = await scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>().GetAsync();
        site.DefaultThemeId.Should().Be("bocchi-mono");
        GetPrivateField<string>(component, "_activeThemeId").Should().Be("bocchi-mono");
        GetPrivateField<bool>(component, "_configDirty").Should().BeTrue();
        navigation.LastUri.Should().BeNull();
    }

    /// <summary>存在 Theme 私有 i18n menu 引用时，切换必须跳迁移向导，不能提前写入默认 Theme。</summary>
    [Fact]
    public async Task SwitchThemeAsync_MigrationRedirectsBeforeSavingTheme()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        await WriteThemeAsync(factory.DataRoot, "next-theme");

        using var scope = factory.Services.CreateScope();
        await SeedThemeMenuReferenceAsync(scope.ServiceProvider);

        var navigation = new RecordingNavigationManager();
        var component = CreateComponent(scope.ServiceProvider, navigation);

        await InvokeSwitchThemeAsync(component, "next-theme");

        navigation.LastUri.Should().Be("/Admin/Site/ThemeMigration?from=bocchi-mono&to=next-theme");
        var site = await scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>().GetAsync();
        site.DefaultThemeId.Should().Be("bocchi-mono");
    }

    /// <summary>无迁移项时只改变 DefaultThemeId，不能把站点基础设置回写成默认值。</summary>
    [Fact]
    public async Task SwitchThemeAsync_NoMigrationPreservesSiteProfileAndChangesTheme()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }
        await WriteThemeAsync(factory.DataRoot, "next-theme");

        using var scope = factory.Services.CreateScope();
        var siteProfile = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        await siteProfile.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = "Custom Site",
            DefaultTitle = "Custom Default Title",
            Description = "Custom Description",
            PublicBaseUrl = "https://custom.example/blog/",
            CopyrightNotice = "Copyright 2026 Custom Site.",
            Language = "en-US",
            TimeZone = "UTC",
            DefaultThemeId = "bocchi-mono",
        });

        var navigation = new RecordingNavigationManager();
        var component = CreateComponent(scope.ServiceProvider, navigation);

        await InvokeSwitchThemeAsync(component, "next-theme");

        var site = await siteProfile.GetAsync();
        site.SiteName.Should().Be("Custom Site");
        site.DefaultTitle.Should().Be("Custom Default Title");
        site.Description.Should().Be("Custom Description");
        site.PublicBaseUrl.Should().Be("https://custom.example/blog/");
        site.CopyrightNotice.Should().Be("Copyright 2026 Custom Site.");
        site.Language.Should().Be("en-US");
        site.TimeZone.Should().Be("UTC");
        site.DefaultThemeId.Should().Be("next-theme");
        GetPrivateField<string>(component, "_activeThemeId").Should().Be("next-theme");
    }

    /// <summary>用真实 DI 服务构造组件实例，仅替换 NavigationManager 以记录跳转目标。</summary>
    private static ThemeCustomization CreateComponent(IServiceProvider services, NavigationManager navigation)
    {
        var component = new ThemeCustomization();
        SetInjectedProperty(component, "SiteProfileSettings", services.GetRequiredService<SiteProfileSettingsService>());
        SetInjectedProperty(component, "ThemeSettings", services.GetRequiredService<ThemeSettingsService>());
        SetInjectedProperty(component, "ThemeMigration", services.GetRequiredService<ThemeMigrationService>());
        SetInjectedProperty(component, "LocalizationSettings", services.GetRequiredService<LocalizationSettingsService>());
        SetInjectedProperty(component, "Navigation", navigation);
        SetInjectedProperty(component, "I18n", services.GetRequiredService<DashboardLocalizationService>());
        SetPrivateField(component, "_activeThemeId", "bocchi-mono");
        return component;
    }

    /// <summary>通过反射调用组件私有事件处理器，避免为测试公开 UI 专用 API。</summary>
    private static async Task InvokeSwitchThemeAsync(ThemeCustomization component, string themeId)
    {
        var method = typeof(ThemeCustomization).GetMethod("SwitchThemeAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = (Task)method!.Invoke(component, [themeId])!;
        await task;
    }

    /// <summary>设置 Razor @inject 生成的属性；这些属性在运行时仍由 Blazor 按同名注入。</summary>
    private static void SetInjectedProperty(ThemeCustomization component, string name, object value)
    {
        var property = typeof(ThemeCustomization).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        property!.SetValue(component, value);
    }

    /// <summary>设置组件私有字段，用于构造特定编辑状态。</summary>
    private static void SetPrivateField<T>(ThemeCustomization component, string name, T value)
    {
        var field = typeof(ThemeCustomization).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(component, value);
    }

    /// <summary>读取组件私有字段，验证事件处理器没有重置关键状态。</summary>
    private static T GetPrivateField<T>(ThemeCustomization component, string name)
    {
        var field = typeof(ThemeCustomization).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T)field!.GetValue(component)!;
    }

    /// <summary>写入一个最小可解析 Theme，模拟用户已安装且无私有迁移项的目标 Theme。</summary>
    private static async Task WriteThemeAsync(string dataRoot, string id)
    {
        var root = Path.Combine(dataRoot, "themes", id);
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "theme.json"),
            $$"""
            {
              "id": "{{id}}",
              "name": "Next Theme",
              "version": "1.0.0",
              "contractVersion": "1.0",
              "runner": {
                "kind": "fluid-static",
                "entry": "fluid"
              }
            }
            """);
    }

    /// <summary>写入一个引用默认 Theme 私有文案的菜单项，触发 ThemeMigrationService 扫描结果。</summary>
    private static async Task SeedThemeMenuReferenceAsync(IServiceProvider services)
    {
        var menu = services.GetRequiredService<NavigationMenuService>();
        await menu.SaveAsync(
        [
            new NavigationEditorItem
            {
                Id = "theme-ref",
                Label = ThemeMigrationService.ThemeRefPrefix + "theme.bocchi-mono.colophonBuiltWith",
                TargetType = "builtin",
                TargetValue = "home",
            },
        ]);
    }

    /// <summary>测试用 NavigationManager，保留相对跳转值方便断言迁移页面路径。</summary>
    private sealed class RecordingNavigationManager : NavigationManager
    {
        public RecordingNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/Admin/Site/Theme");
        }

        /// <summary>最后一次 NavigateTo 收到的原始 URI。</summary>
        public string? LastUri { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            LastUri = uri;
        }
    }
}
