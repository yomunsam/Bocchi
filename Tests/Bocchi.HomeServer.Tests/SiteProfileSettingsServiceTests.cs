using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

/// <summary>workspace site.yaml 权威：启动灌库、禁止默认值盖文件、过新 formatVersion 拒绝写入。</summary>
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

    [Fact]
    public async Task SaveAsync_NewerFormatVersion_RefusesWithoutChangingFileOrDb()
    {
        await using var factory = new IsolatedDataRootWebApplicationFactory();
        _ = factory.Services;
        var siteYaml = Path.Combine(factory.DataRoot, "workspace", "site", "site.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(siteYaml)!);
        const string newer = """
            formatVersion: 99
            title: Future Site
            defaultTitle: Future
            language: en-US
            timeZone: UTC
            baseUrl: https://future.example/
            copyright: Copyright © 2099 Future.
            defaultThemeId: bocchi-mono
            """;
        await File.WriteAllTextAsync(siteYaml, newer);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        var before = await service.GetAsync();
        var beforeName = before.SiteName;

        var act = async () => await service.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = "Should Not Save",
            DefaultTitle = "Nope",
            Language = "zh-CN",
            TimeZone = "Asia/Shanghai",
            PublicBaseUrl = "https://nope.example/",
            CopyrightNotice = "Nope",
            DefaultThemeId = "bocchi-mono",
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*formatVersion=99*");

        var onDisk = await File.ReadAllTextAsync(siteYaml);
        onDisk.Should().Be(newer);
        onDisk.Should().Contain("formatVersion: 99");
        onDisk.Should().NotContain("Should Not Save");

        var after = await service.GetAsync();
        after.SiteName.Should().Be(beforeName);
        after.SiteName.Should().NotBe("Should Not Save");
    }

    [Fact]
    public async Task EnsureAsync_NewerFormatVersion_RefusesImportAndLeavesFile()
    {
        // 独立 DataRoot，避免污染同 fixture 的其他用例。
        await using var factory = new IsolatedDataRootWebApplicationFactory();
        _ = factory.Services; // 触发宿主构建
        var siteYaml = Path.Combine(factory.DataRoot, "workspace", "site", "site.yaml");
        Directory.CreateDirectory(Path.GetDirectoryName(siteYaml)!);
        const string newer = """
            formatVersion: 99
            title: Future Import
            defaultTitle: Future Import
            language: ja-JP
            timeZone: Asia/Tokyo
            baseUrl: https://future-import.example/
            copyright: Copyright © 2099 Future.
            defaultThemeId: bocchi-mono
            """;
        await File.WriteAllTextAsync(siteYaml, newer);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();

        var act = async () => await service.EnsureAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*formatVersion=99*");

        var onDisk = await File.ReadAllTextAsync(siteYaml);
        onDisk.Should().Be(newer);
        onDisk.Should().Contain("formatVersion: 99");

        var settings = await service.GetAsync();
        settings.SiteName.Should().NotBe("Future Import");
    }
}
