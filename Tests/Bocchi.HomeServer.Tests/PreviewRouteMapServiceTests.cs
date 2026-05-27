using Bocchi.HomeServer.Services;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

public sealed class PreviewRouteMapServiceTests
{
    [Fact]
    public async Task FindAsync_MapsLanguagePrefixedPostRouteToVariantEditUrl()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        var postDir = Path.Combine(factory.DataRoot, "workspace", "posts", "2026", "hello-preview");
        Directory.CreateDirectory(postDir);
        await File.WriteAllTextAsync(
            Path.Combine(postDir, "index.md"),
            """
            ---
            title: Hello Preview
            slug: hello-preview
            status: Published
            ---
            Primary body.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(postDir, "index.zh-TW.md"),
            """
            ---
            title: Hello Preview Traditional
            slug: hello-preview
            status: Published
            language: zh-TW
            localization:
              translationOf:
                language: zh-CN
            ---
            Traditional body.
            """);

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var localization = services.GetRequiredService<LocalizationSettingsService>();
        await localization.SaveAsync("zh-CN", ["zh-CN", "zh-TW"], []);
        await services.GetRequiredService<ContentScanner>().ScanAsync();

        var routeMap = services.GetRequiredService<PreviewRouteMapService>();
        var match = await routeMap.FindAsync("/zh-TW/posts/2026/hello-preview/");

        match.Should().NotBeNull();
        match!.Route.Should().Be("/zh-TW/posts/2026/hello-preview/");
        match.EditUrl.Should().Contain("posts%2F2026%2Fhello-preview%2Findex.zh-TW.md");
    }

    [Fact]
    public async Task FindAsync_KeepsPrimaryLanguageRouteUnprefixed()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        factory.SeedPublishedPostWithMedia();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var localization = services.GetRequiredService<LocalizationSettingsService>();
        await localization.SaveAsync("zh-CN", ["zh-CN", "zh-TW"], []);
        await services.GetRequiredService<ContentScanner>().ScanAsync();

        var routeMap = services.GetRequiredService<PreviewRouteMapService>();
        var match = await routeMap.FindAsync("/posts/2026/hello-preview/");

        match.Should().NotBeNull();
        match!.Route.Should().Be("/posts/2026/hello-preview/");
        match.EditUrl.Should().Contain("posts%2F2026%2Fhello-preview%2Findex.md");
    }

    /// <summary>验证 Page / Work 的语言前缀 route 会回到同目录下的语言 variant 源文件。</summary>
    [Fact]
    public async Task FindAsync_MapsLanguagePrefixedPageAndWorkRoutesToVariantEditUrls()
    {
        using var factory = new IsolatedDataRootWebApplicationFactory();
        using (await factory.CreateAdminClientAsync())
        {
        }

        var pageDir = Path.Combine(factory.DataRoot, "workspace", "pages", "about-preview");
        Directory.CreateDirectory(pageDir);
        await File.WriteAllTextAsync(
            Path.Combine(pageDir, "index.md"),
            """
            ---
            title: About Preview
            slug: about-preview
            status: Published
            ---
            Primary page body.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(pageDir, "index.zh-TW.md"),
            """
            ---
            title: About Preview Traditional
            slug: about-preview
            status: Published
            language: zh-TW
            localization:
              translationOf:
                language: zh-CN
            ---
            Traditional page body.
            """);

        var workDir = Path.Combine(factory.DataRoot, "workspace", "works", "2026", "portfolio-preview");
        Directory.CreateDirectory(workDir);
        await File.WriteAllTextAsync(
            Path.Combine(workDir, "index.md"),
            """
            ---
            title: Portfolio Preview
            slug: portfolio-preview
            status: Published
            role: Maker
            ---
            Primary work body.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workDir, "index.zh-TW.md"),
            """
            ---
            title: Portfolio Preview Traditional
            slug: portfolio-preview
            status: Published
            role: Maker
            language: zh-TW
            localization:
              translationOf:
                language: zh-CN
            ---
            Traditional work body.
            """);

        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var localization = services.GetRequiredService<LocalizationSettingsService>();
        await localization.SaveAsync("zh-CN", ["zh-CN", "zh-TW"], []);
        await services.GetRequiredService<ContentScanner>().ScanAsync();

        var routeMap = services.GetRequiredService<PreviewRouteMapService>();
        var pageMatch = await routeMap.FindAsync("/zh-TW/about-preview/");
        var workMatch = await routeMap.FindAsync("/zh-TW/works/2026/portfolio-preview/");

        pageMatch.Should().NotBeNull();
        pageMatch!.Route.Should().Be("/zh-TW/about-preview/");
        pageMatch.EditUrl.Should().Contain("pages%2Fabout-preview%2Findex.zh-TW.md");
        workMatch.Should().NotBeNull();
        workMatch!.Route.Should().Be("/zh-TW/works/2026/portfolio-preview/");
        workMatch.EditUrl.Should().Contain("works%2F2026%2Fportfolio-preview%2Findex.zh-TW.md");
    }
}
