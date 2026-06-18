using System.IO.Compression;

using Bocchi.Generator.Theme;
using Bocchi.Workspace;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bocchi.Generator.Tests;

/// <summary>ThemePackageService 的 zip inspection、安装和更新回滚契约测试。</summary>
public sealed class ThemePackageServiceTests
{
    /// <summary>zip 根目录直接包含 theme.json 时，应识别出可安装的 fluid-static Theme。</summary>
    [Fact]
    public async Task InspectZipAsync_RootThemeZipRecognizesFluidStaticTheme()
    {
        using var temp = new TempPackageDataRoot();
        var zipPath = Path.Combine(temp.Root, "theme.zip");
        await WriteThemeZipAsync(zipPath, id: "my-theme", version: "1.0.0");
        var service = CreateService(temp.Layout);

        var inspection = await service.InspectZipAsync(zipPath);

        inspection.IsInstallable.Should().BeTrue();
        inspection.ThemeId.Should().Be("my-theme");
        inspection.Name.Should().Be("My Theme");
        inspection.Version.Should().Be("1.0.0");
        inspection.RunnerKind.Should().Be("fluid-static");
        File.Exists(Path.Combine(inspection.SourceRoot, "theme.json")).Should().BeTrue();
    }

    /// <summary>GitHub 下载式单顶层目录 zip 应被归一化成 staging Theme Root。</summary>
    [Fact]
    public async Task InspectZipAsync_SingleTopDirectoryZipRecognizesThemeRoot()
    {
        using var temp = new TempPackageDataRoot();
        var zipPath = Path.Combine(temp.Root, "github-theme.zip");
        await WriteThemeZipAsync(zipPath, id: "my-theme", version: "1.0.0", rootPrefix: "my-theme-main");
        var service = CreateService(temp.Layout);

        var inspection = await service.InspectZipAsync(zipPath);

        inspection.IsInstallable.Should().BeTrue();
        File.Exists(Path.Combine(inspection.SourceRoot, "theme.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(inspection.SourceRoot, "my-theme-main")).Should().BeFalse();
    }

    /// <summary>Zip Slip 路径必须在写入 themes 目录之前被阻断。</summary>
    [Fact]
    public async Task InspectZipAsync_RejectsTraversalEntry()
    {
        using var temp = new TempPackageDataRoot();
        var zipPath = Path.Combine(temp.Root, "evil.zip");
        await WriteThemeZipAsync(zipPath, id: "my-theme", version: "1.0.0", extraEntries: [("../evil.txt", "owned")]);
        var service = CreateService(temp.Layout);

        var inspection = await service.InspectZipAsync(zipPath);

        inspection.IsInstallable.Should().BeFalse();
        inspection.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-package-entry-traversal");
        File.Exists(Path.Combine(temp.Layout.ThemesDirectory, "evil.txt")).Should().BeFalse();
    }

    /// <summary>process runner 包可被 inspection 识别，但安装前必须显式确认 trust。</summary>
    [Fact]
    public async Task InstallOrUpdateAsync_ProcessRunnerRequiresTrustConfirmation()
    {
        using var temp = new TempPackageDataRoot();
        var zipPath = Path.Combine(temp.Root, "process-theme.zip");
        await WriteThemeZipAsync(zipPath, id: "process-theme", version: "1.0.0", runnerKind: "process");
        var service = CreateService(temp.Layout);
        var inspection = await service.InspectZipAsync(zipPath);

        var act = () => service.InstallOrUpdateAsync(inspection, trustProcessRunner: false);

        inspection.RequiresTrust.Should().BeTrue();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*必须显式确认信任*");
    }

    /// <summary>更新同 id Theme 应替换文件，同时不触碰 state/theme-config 下的配置。</summary>
    [Fact]
    public async Task InstallOrUpdateAsync_UpdateReplacesFilesAndPreservesThemeConfig()
    {
        using var temp = new TempPackageDataRoot();
        var configPath = Path.Combine(temp.Layout.ThemeConfigDirectory, "my-theme.json");
        Directory.CreateDirectory(temp.Layout.ThemeConfigDirectory);
        await File.WriteAllTextAsync(configPath, """{"accent":"pink"}""");
        await WriteThemeZipAsync(Path.Combine(temp.Root, "theme-v1.zip"), id: "my-theme", version: "1.0.0", assetContent: "v1");
        await WriteThemeZipAsync(Path.Combine(temp.Root, "theme-v2.zip"), id: "my-theme", version: "2.0.0", assetContent: "v2");
        var service = CreateService(temp.Layout);
        await service.InstallOrUpdateAsync(await service.InspectZipAsync(Path.Combine(temp.Root, "theme-v1.zip")), trustProcessRunner: false);

        var result = await service.InstallOrUpdateAsync(
            await service.InspectZipAsync(Path.Combine(temp.Root, "theme-v2.zip")),
            trustProcessRunner: false);

        result.WasUpdate.Should().BeTrue();
        result.BackupRoot.Should().NotBeNull();
        File.ReadAllText(Path.Combine(temp.Layout.ThemesDirectory, "my-theme", "assets", "app.css")).Should().Be("v2");
        File.Exists(Path.Combine(result.BackupRoot!, "assets", "app.css")).Should().BeTrue();
        File.ReadAllText(configPath).Should().Be("""{"accent":"pink"}""");
    }

    /// <summary>bocchi-mono 作为内置 Theme，不允许被 zip 安装流程覆盖。</summary>
    [Fact]
    public async Task InstallOrUpdateAsync_RejectsBocchiMonoPackage()
    {
        using var temp = new TempPackageDataRoot();
        var zipPath = Path.Combine(temp.Root, "bocchi-mono.zip");
        await WriteThemeZipAsync(zipPath, id: "bocchi-mono", version: "1.0.0");
        var service = CreateService(temp.Layout);
        var inspection = await service.InspectZipAsync(zipPath);

        var act = () => service.InstallOrUpdateAsync(inspection, trustProcessRunner: false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*bocchi-mono*");
    }

    [Fact]
    public async Task InspectZipAsync_RejectsInvalidStaticAssets()
    {
        using var temp = new TempPackageDataRoot();
        var zipPath = Path.Combine(temp.Root, "invalid-static-assets.zip");
        await WriteThemeZipAsync(
            zipPath,
            id: "asset-theme",
            version: "1.0.0",
            staticAssetsJson: """
                [
                  {
                    "from": "../assets",
                    "to": "assets"
                  }
                ]
                """);
        var service = CreateService(temp.Layout);

        var inspection = await service.InspectZipAsync(zipPath);

        inspection.IsInstallable.Should().BeFalse();
        inspection.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-static-assets-from-invalid");
        inspection.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-static-assets-to-relative");
    }

    /// <summary>Package inspection 必须阻止 Theme 声明其他 Theme namespace 下的私有 i18n key。</summary>
    [Fact]
    public async Task InspectZipAsync_RejectsInvalidPrivateI18nNamespace()
    {
        using var temp = new TempPackageDataRoot();
        var zipPath = Path.Combine(temp.Root, "wrong-namespace.zip");
        await WriteThemeZipAsync(
            zipPath,
            id: "cozy",
            version: "1.0.0",
            i18nKey: "theme.bocchi-mono.cardLabel");
        var service = CreateService(temp.Layout);

        var inspection = await service.InspectZipAsync(zipPath);

        inspection.IsInstallable.Should().BeFalse();
        inspection.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "theme-i18n-key-namespace-invalid");
    }

    private static ThemePackageService CreateService(BocchiDataLayout layout)
        => new(
            layout,
            Options.Create(new ThemePackageOptions()),
            NullLogger<ThemePackageService>.Instance);

    private static async Task WriteThemeZipAsync(
        string zipPath,
        string id,
        string version,
        string rootPrefix = "",
        string runnerKind = "fluid-static",
        string assetContent = "body{}",
        string? staticAssetsJson = null,
        string? i18nKey = null,
        IReadOnlyList<(string Path, string Content)>? extraEntries = null)
    {
        var prefix = string.IsNullOrWhiteSpace(rootPrefix) ? string.Empty : rootPrefix.TrimEnd('/') + "/";
        await using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        await WriteEntryAsync(archive, prefix + "theme.json", CreateManifestJson(id, version, runnerKind, staticAssetsJson, i18nKey));
        await WriteEntryAsync(archive, prefix + "assets/app.css", assetContent);
        foreach (var (path, content) in extraEntries ?? [])
        {
            await WriteEntryAsync(archive, path, content);
        }
    }

    private static string CreateManifestJson(string id, string version, string runnerKind, string? staticAssetsJson, string? i18nKey)
    {
        var staticAssetsBlock = string.IsNullOrWhiteSpace(staticAssetsJson)
            ? string.Empty
            : $",\n  \"staticAssets\": {staticAssetsJson}";
        var i18nBlock = i18nKey is null
            ? string.Empty
            : $$"""
              ,
              "i18n": {
                "keys": [
                  {
                    "key": "{{i18nKey}}",
                    "title": "Test key",
                    "defaultValues": {
                      "en-US": "Test"
                    }
                  }
                ]
              }
              """;
        if (string.Equals(runnerKind, "process", StringComparison.OrdinalIgnoreCase))
        {
            return $$"""
            {
              "id": "{{id}}",
              "name": "{{ToDisplayName(id)}}",
              "version": "{{version}}",
              "contractVersion": "1.0",
              "runner": {
                "kind": "process",
                "command": "echo ok"
              }{{staticAssetsBlock}}{{i18nBlock}}
            }
            """;
        }

        return $$"""
        {
          "id": "{{id}}",
          "name": "{{ToDisplayName(id)}}",
          "version": "{{version}}",
          "contractVersion": "1.0",
          "runner": {
            "kind": "fluid-static",
            "entry": "fluid"
          }{{staticAssetsBlock}}{{i18nBlock}}
        }
        """;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream);
        await writer.WriteAsync(content);
    }

    private static string ToDisplayName(string id)
        => string.Join(
            ' ',
            id.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>Theme Package 测试专用 DataRoot。</summary>
    private sealed class TempPackageDataRoot : IDisposable
    {
        /// <summary>临时 DataRoot 物理路径。</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "bocchi-theme-package-tests", Guid.NewGuid().ToString("N"));

        /// <summary>临时 DataRoot 布局。</summary>
        public BocchiDataLayout Layout { get; }

        public TempPackageDataRoot()
        {
            Layout = new BocchiDataLayout(Root);
            Directory.CreateDirectory(Layout.ThemesDirectory);
            Directory.CreateDirectory(Layout.ThemeUploadCacheDirectory);
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
