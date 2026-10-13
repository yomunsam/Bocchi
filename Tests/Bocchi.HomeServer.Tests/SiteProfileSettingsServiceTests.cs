using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

/// <summary>workspace site.yaml 权威：启动灌库、禁止默认值盖文件。</summary>
public sealed class SiteProfileSettingsServiceTests : IClassFixture<IsolatedDataRootWebApplicationFactory>
{
    private readonly IsolatedDataRootWebApplicationFactory _factory;

    public SiteProfileSettingsServiceTests(IsolatedDataRootWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EnsureAsync_ImportsExistingWorkspaceSiteYamlIntoDb()
    {
        var siteYaml = Path.Combine(_factory.DataRoot, "workspace", "site", "site.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(siteYaml)!);
        await File.WriteAllTextAsync(siteYaml, """
            formatVersion: 1
            title: Restored Site
            defaultTitle: Restored Title
            description: From workspace
            language: en-US
            timeZone: Asia/Singapore
            baseUrl: https://restored.example/
            copyright: Copyright © 2026 Restored.
            defaultThemeId: bocchi-mono
            """);

        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        await service.EnsureAsync();

        var settings = await service.GetAsync();
        settings.SiteName.Should().Be("Restored Site");
        settings.DefaultTitle.Should().Be("Restored Title");
        settings.Description.Should().Be("From workspace");
        settings.Language.Should().Be("en-US");
        settings.TimeZone.Should().Be("Asia/Singapore");
        settings.PublicBaseUrl.Should().Be("https://restored.example/");

        var onDisk = await File.ReadAllTextAsync(siteYaml);
        onDisk.Should().Contain("title: Restored Site");
        onDisk.Should().NotContain("title: Bocchi");
    }

    [Fact]
    public async Task SaveAsync_WritesFormatVersionAndWorkspaceFirst()
    {
        ResetSupportedSiteYaml();
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        await service.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = "Saved Site",
            DefaultTitle = "Saved Title",
            Language = "zh-CN",
            TimeZone = "Asia/Shanghai",
            PublicBaseUrl = "https://saved.example/",
            CopyrightNotice = "Copyright © 2026 Saved.",
            DefaultThemeId = "bocchi-mono",
        });

        var siteYaml = Path.Combine(_factory.DataRoot, "workspace", "site", "site.yaml");
        var onDisk = await File.ReadAllTextAsync(siteYaml);
        onDisk.Should().Contain("formatVersion:");
        onDisk.Should().Contain("title: Saved Site");
        onDisk.Should().Contain("baseUrl: https://saved.example/");
    }

    /// <summary>过新 formatVersion 时保存不得改文件，也不得改数据库投影。</summary>
    [Fact]
    public async Task SaveAsync_RefusesWhenFormatVersionIsNewerThanCurrent()
    {
        ResetSupportedSiteYaml();
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        var prepared = await service.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = "Before Refuse",
            DefaultTitle = "Before Refuse",
            Language = "zh-CN",
            TimeZone = "Asia/Shanghai",
            CopyrightNotice = "Copyright © 2026 Before.",
            DefaultThemeId = "bocchi-mono",
        });
        prepared.Succeeded.Should().BeTrue();

        var siteYaml = Path.Combine(_factory.DataRoot, "workspace", "site", "site.yaml");
        const string future = """
            formatVersion: 99
            title: Future Site
            language: ja-JP
            """;
        await File.WriteAllTextAsync(siteYaml, future);

        var refused = await service.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = "Should Not Stick",
            DefaultTitle = "Should Not Stick",
            Language = "en-US",
            TimeZone = "UTC",
            CopyrightNotice = "nope",
            DefaultThemeId = "other",
        });

        refused.Succeeded.Should().BeFalse();
        refused.Code.Should().Be(WorkspaceFormatVersion.UnsupportedCode);
        (await File.ReadAllTextAsync(siteYaml)).Should().Be(future);
        var settings = await service.GetAsync();
        settings.SiteName.Should().Be("Before Refuse");
        settings.Language.Should().Be("zh-CN");
    }

    /// <summary>启动灌库遇到过新 formatVersion 时保留文件和已有数据库投影。</summary>
    [Fact]
    public async Task EnsureAsync_RefusesImportWhenFormatVersionIsNewerThanCurrent()
    {
        ResetSupportedSiteYaml();
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        var prepared = await service.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = "Db Stays",
            DefaultTitle = "Db Stays",
            Language = "zh-CN",
            TimeZone = "Asia/Shanghai",
            PublicBaseUrl = "https://stays.example/",
            CopyrightNotice = "Copyright © 2026 Stays.",
            DefaultThemeId = "bocchi-mono",
        });
        prepared.Succeeded.Should().BeTrue();

        var siteYaml = Path.Combine(_factory.DataRoot, "workspace", "site", "site.yaml");
        const string future = """
            formatVersion: 99
            title: Imported Should Not
            defaultTitle: Future Title
            description: newer workspace
            language: fr-FR
            timeZone: Europe/Paris
            baseUrl: https://future.example/
            copyright: Copyright © 2099 Future.
            defaultThemeId: future-theme
            """;
        await File.WriteAllTextAsync(siteYaml, future);

        await service.EnsureAsync();

        (await File.ReadAllTextAsync(siteYaml)).Should().Be(future);
        var settings = await service.GetAsync();
        settings.SiteName.Should().Be("Db Stays");
        settings.Language.Should().Be("zh-CN");
        settings.PublicBaseUrl.Should().Be("https://stays.example/");
        settings.DefaultThemeId.Should().Be("bocchi-mono");
    }

    /// <summary>这组测试共用一个 DataRoot，先把 site.yaml 放回当前版本，避免上一条拒绝用例挡住后面的保存。</summary>
    private void ResetSupportedSiteYaml()
    {
        var siteYaml = Path.Combine(_factory.DataRoot, "workspace", "site", "site.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(siteYaml)!);
        File.WriteAllText(siteYaml, """
            formatVersion: 1
            title: Seed
            defaultTitle: Seed
            language: zh-CN
            timeZone: Asia/Shanghai
            defaultThemeId: bocchi-mono
            """);
    }
}
