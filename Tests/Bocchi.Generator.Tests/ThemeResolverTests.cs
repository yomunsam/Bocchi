using Bocchi.Generator.Theme;
using Bocchi.Theme.DefaultStatic;
using Bocchi.Workspace;

using Microsoft.Extensions.Options;

namespace Bocchi.Generator.Tests;

/// <summary>ThemeResolver 统一 Theme 来源解析的核心契约测试。</summary>
public sealed class ThemeResolverTests
{
    /// <summary>默认 Theme 应由 Resolver 物化，同时普通 installed Theme 应进入可用 Catalog。</summary>
    [Fact]
    public async Task ListAvailableThemesAsync_MaterializesDefaultStaticAndInstalledTheme()
    {
        using var temp = new TempThemeDataRoot();
        await WriteThemeAsync(Path.Combine(temp.Layout.ThemesDirectory, "my-theme"), "my-theme", "1.2.3");
        var resolver = CreateResolver(temp.Layout);

        var catalog = await resolver.ListAvailableThemesAsync();

        catalog.Should().Contain(item =>
            item.Id == DefaultStaticThemeDefinition.ThemeId &&
            item.SourceKind == ThemeSourceKind.BuiltIn &&
            item.IsAvailable);
        catalog.Should().Contain(item =>
            item.Id == "my-theme" &&
            item.Name == "My Theme" &&
            item.Version == "1.2.3" &&
            item.SourceKind == ThemeSourceKind.Installed &&
            item.IsAvailable);
    }

    /// <summary>Development 环境默认启用 Dev Link，并允许它覆盖同 id 的 installed Theme。</summary>
    [Fact]
    public async Task ResolveThemeAsync_DevelopmentDevLinkShadowsInstalledTheme()
    {
        using var temp = new TempThemeDataRoot();
        await WriteThemeAsync(Path.Combine(temp.Layout.ThemesDirectory, "my-theme"), "my-theme", "1.0.0");
        var externalRoot = Path.Combine(temp.Root, "external-theme");
        await WriteThemeAsync(externalRoot, "my-theme", "2.0.0");
        await WriteDevLinksAsync(temp.Layout.ThemesDirectory, externalRoot);
        var resolver = CreateResolver(temp.Layout, environmentName: "Development");

        var result = await resolver.ResolveThemeAsync("my-theme");
        var catalog = await resolver.ListAvailableThemesAsync();

        result.Theme.Should().NotBeNull();
        result.Theme!.SourceKind.Should().Be(ThemeSourceKind.DevLink);
        result.Theme.Root.Should().Be(Path.GetFullPath(externalRoot));
        result.Theme.Version.Should().Be("2.0.0");
        result.Theme.ShadowsInstalledTheme.Should().BeTrue();
        catalog.Should().Contain(item =>
            item.Id == "my-theme" &&
            item.SourceKind == ThemeSourceKind.DevLink &&
            item.ShadowsInstalledTheme);
    }

    /// <summary>Production 默认忽略 dev-links.json，除非配置显式允许 Dev Link。</summary>
    [Fact]
    public async Task ResolveThemeAsync_ProductionIgnoresDevLinksByDefault()
    {
        using var temp = new TempThemeDataRoot();
        await WriteThemeAsync(Path.Combine(temp.Layout.ThemesDirectory, "my-theme"), "my-theme", "1.0.0");
        var externalRoot = Path.Combine(temp.Root, "external-theme");
        await WriteThemeAsync(externalRoot, "my-theme", "2.0.0");
        await WriteDevLinksAsync(temp.Layout.ThemesDirectory, externalRoot);
        var resolver = CreateResolver(temp.Layout, environmentName: "Production");

        var result = await resolver.ResolveThemeAsync("my-theme");

        result.Theme.Should().NotBeNull();
        result.Theme!.SourceKind.Should().Be(ThemeSourceKind.Installed);
        result.Theme.Version.Should().Be("1.0.0");
    }

    /// <summary>启用的 Dev Link 若与 installed Theme 同 id，Dev Link 错误不应静默回落到 installed Theme。</summary>
    [Fact]
    public async Task ResolveThemeAsync_InvalidDevLinkDoesNotFallbackToInstalledTheme()
    {
        using var temp = new TempThemeDataRoot();
        await WriteThemeAsync(Path.Combine(temp.Layout.ThemesDirectory, "my-theme"), "my-theme", "1.0.0");
        var missingExternalRoot = Path.Combine(temp.Root, "missing-external-theme");
        await WriteDevLinksAsync(temp.Layout.ThemesDirectory, missingExternalRoot);
        var resolver = CreateResolver(temp.Layout, environmentName: "Development");

        var result = await resolver.ResolveThemeAsync("my-theme");

        result.Theme.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-root-missing");
    }

    /// <summary>单个坏 Theme 不能中断 Catalog 列表；Dashboard 需要拿到可解释诊断。</summary>
    [Fact]
    public async Task ListAvailableThemesAsync_InvalidManifestReturnsDiagnosticItem()
    {
        using var temp = new TempThemeDataRoot();
        var brokenRoot = Path.Combine(temp.Layout.ThemesDirectory, "broken-theme");
        Directory.CreateDirectory(brokenRoot);
        await File.WriteAllTextAsync(Path.Combine(brokenRoot, "theme.json"), "{ broken");
        var resolver = CreateResolver(temp.Layout);

        var catalog = await resolver.ListAvailableThemesAsync();

        var item = catalog.Should().ContainSingle(x => x.Id == "broken-theme").Subject;
        item.IsAvailable.Should().BeFalse();
        item.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-manifest-json-invalid");
    }

    [Fact]
    public async Task ListAvailableThemesAsync_InvalidStaticAssetsReturnsDiagnosticItem()
    {
        using var temp = new TempThemeDataRoot();
        var root = Path.Combine(temp.Layout.ThemesDirectory, "asset-theme");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "theme.json"), """
            {
              "id": "asset-theme",
              "name": "Asset Theme",
              "version": "0.1.0",
              "contractVersion": "1.0",
              "outputDir": "build",
              "runner": {
                "kind": "fluid-static",
                "entry": "fluid"
              },
              "staticAssets": [
                {
                  "from": "../assets",
                  "to": "assets"
                },
                {
                  "from": "build",
                  "to": "/build-assets"
                }
              ]
            }
            """);
        Directory.CreateDirectory(Path.Combine(root, "build"));
        var resolver = CreateResolver(temp.Layout);

        var catalog = await resolver.ListAvailableThemesAsync();

        var item = catalog.Should().ContainSingle(x => x.Id == "asset-theme").Subject;
        item.IsAvailable.Should().BeFalse();
        item.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-static-assets-from-invalid");
        item.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-static-assets-to-relative");
        item.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-static-assets-from-output");
    }

    [Fact]
    public async Task ListAvailableThemesAsync_StaticAssetSymlinkOutsideRootReturnsDiagnosticItem()
    {
        using var temp = new TempThemeDataRoot();
        var root = Path.Combine(temp.Layout.ThemesDirectory, "asset-theme");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "theme.json"), """
            {
              "id": "asset-theme",
              "name": "Asset Theme",
              "version": "0.1.0",
              "contractVersion": "1.0",
              "runner": {
                "kind": "fluid-static",
                "entry": "fluid"
              },
              "staticAssets": [
                {
                  "from": "assets",
                  "to": "/assets"
                }
              ]
            }
            """);
        var external = Path.Combine(temp.Root, "external-assets");
        Directory.CreateDirectory(external);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "assets"), external);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }
        var resolver = CreateResolver(temp.Layout);

        var catalog = await resolver.ListAvailableThemesAsync();

        var item = catalog.Should().ContainSingle(x => x.Id == "asset-theme").Subject;
        item.IsAvailable.Should().BeFalse();
        item.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-static-assets-link-outside-root");
    }

    private static ThemeResolver CreateResolver(
        BocchiDataLayout layout,
        string environmentName = "Production",
        bool? allowDevLinks = null)
        => new(
            layout,
            Options.Create(new ThemeDevelopmentOptions
            {
                EnvironmentName = environmentName,
                AllowDevLinks = allowDevLinks,
            }));

    private static async Task WriteThemeAsync(string root, string id, string version)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "theme.json"),
            $$"""
            {
              "id": "{{id}}",
              "name": "{{ToDisplayName(id)}}",
              "version": "{{version}}",
              "contractVersion": "1.0",
              "runner": {
                "kind": "fluid-static",
                "entry": "fluid"
              }
            }
            """);
    }

    private static async Task WriteDevLinksAsync(string themesDirectory, string root)
    {
        Directory.CreateDirectory(themesDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(themesDirectory, "dev-links.json"),
            $$"""
            {
              "schemaVersion": "1.0",
              "links": [
                {
                  "id": "my-theme",
                  "root": "{{root.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                  "enabled": true,
                  "note": "local development"
                }
              ]
            }
            """);
    }

    private static string ToDisplayName(string id)
        => string.Join(
            ' ',
            id.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>测试专用 DataRoot，确保 Theme 文件不会污染真实工作区。</summary>
    private sealed class TempThemeDataRoot : IDisposable
    {
        /// <summary>临时 DataRoot 物理路径。</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "bocchi-theme-resolver-tests", Guid.NewGuid().ToString("N"));

        /// <summary>临时 DataRoot 布局。</summary>
        public BocchiDataLayout Layout { get; }

        public TempThemeDataRoot()
        {
            Layout = new BocchiDataLayout(Root);
            Directory.CreateDirectory(Layout.ThemesDirectory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
