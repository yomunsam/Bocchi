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
}
