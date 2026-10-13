using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bocchi.HomeServer.Tests;

public sealed class LocalizationSettingsServiceTests
{
    [Fact]
    public async Task SaveAsync_EnsuresPrimaryLanguageIsEnabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);

        await service.SaveAsync("ja-JP", ["en-US"], []);

        var settings = await service.GetAsync();
        settings.PrimaryLanguage.Code.Should().Be("ja-JP");
        settings.EnabledLanguages.Select(x => x.Code).Should().Contain(["en-US", "ja-JP"]);
        settings.UrlPolicy.Should().Be(LocalizationSettingsService.PrimaryUnprefixedUrlPolicy);
    }

    [Fact]
    public async Task SaveAsync_PreservesCustomLanguagesInPicklist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);

        await service.SaveAsync(
            "tok",
            ["tok"],
            [
                new LanguageRecord
                {
                    Code = "tok",
                    NativeName = "Toki Pona",
                    EnglishName = "Toki Pona",
                },
            ]);

        var settings = await service.GetAsync();
        settings.PrimaryLanguage.Code.Should().Be("tok");
        settings.CustomLanguages.Should().ContainSingle(x => x.Code == "tok");
        settings.EnabledLanguages.Should().ContainSingle(x => x.Code == "tok");
    }

    [Fact]
    public async Task SaveCommonTextOverridesAsync_NormalizesOverridesAndExportsBuildSnapshot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);

        await service.SaveAsync("en-US", ["zh-CN"], []);
        await service.SaveCommonTextOverridesAsync(
        [
            new CommonI18nTextOverride
            {
                Key = " menu.home ",
                Values = new Dictionary<string, string>
                {
                    [" en-US "] = " Start ",
                    ["zh-CN"] = " ",
                    [""] = "ignored",
                },
            },
            new CommonI18nTextOverride
            {
                Key = "content.translationNotice",
                Values = new Dictionary<string, string>
                {
                    ["zh-CN"] = "正在查看译文",
                },
            },
        ]);

        var settings = await service.GetAsync();
        settings.CommonTextOverrides.Should().ContainSingle(x =>
            x.Key == "menu.home"
            && x.Values.ContainsKey("en-US")
            && x.Values["en-US"] == "Start");
        settings.CommonTextOverrides.Should().ContainSingle(x =>
            x.Key == "content.translationNotice"
            && x.Values.ContainsKey("zh-CN"));

        var snapshot = await service.GetBuildLocalizationOptionsAsync();
        snapshot.PrimaryLanguage.Should().Be("en-US");
        snapshot.EnabledLanguages.Select(x => x.Code).Should().Contain(["zh-CN", "en-US"]);
        snapshot.UrlPolicy.Should().Be(LocalizationSettingsService.PrimaryUnprefixedUrlPolicy);
        snapshot.Text["menu.home"]["en-US"].Should().Be("Start");
        snapshot.Text["content.translationNotice"]["zh-CN"].Should().Be("正在查看译文");
    }

    /// <summary>没有文件时从数据库导出，commonTextOverrides 要在 YAML 里，UpdatedAt 不要。</summary>
    [Fact]
    public async Task EnsureAsync_ExportsDatabaseWhenFileIsMissing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        db.LocalizationSettings.Add(new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = "en-US",
            EnabledLanguagesJson = """[{"code":"en-US","nativeName":"English","englishName":"English"},{"code":"zh-CN","nativeName":"简体中文","englishName":"Simplified Chinese"}]""",
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = """{"common.readMore":{"en-US":"Read more"}}""",
            UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
        });
        await db.SaveChangesAsync();
        Directory.CreateDirectory(workspace.Layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile, "formatVersion: 1\nlanguage: en-US\ntitle: Demo\n");

        await service.EnsureAsync();

        var yaml = await File.ReadAllTextAsync(workspace.Layout.Workspace.LocalizationFile);
        yaml.Should().Contain("primaryLanguage: en-US");
        yaml.Should().Contain("common.readMore:");
        yaml.Should().Contain("Read more");
        yaml.Should().NotContain("updatedAt");
        var settings = await service.GetAsync();
        settings.CommonTextOverrides.Should().ContainSingle(item =>
            item.Key == "common.readMore" && item.Values["en-US"] == "Read more");
    }

    /// <summary>已有文件时以文件灌库。</summary>
    [Fact]
    public async Task EnsureAsync_ImportsFileOverDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        db.LocalizationSettings.Add(new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = "zh-CN",
            EnabledLanguagesJson = """[{"code":"zh-CN","nativeName":"简体中文","englishName":"Simplified Chinese"}]""",
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = "{}",
            UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
        });
        await db.SaveChangesAsync();
        Directory.CreateDirectory(workspace.Layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile, "formatVersion: 1\nlanguage: zh-CN\ntitle: Demo\n");
        await File.WriteAllTextAsync(workspace.Layout.Workspace.LocalizationFile, """
            primaryLanguage: en-US
            urlPolicy: PrimaryUnprefixed
            enabledLanguages:
              - code: en-US
                nativeName: English
                englishName: English
              - code: zh-CN
                nativeName: 简体中文
                englishName: Simplified Chinese
            customLanguages: []
            commonTextOverrides:
              menu.about:
                zh-CN: 关于
                en-US: About
            """);

        await service.EnsureAsync();

        var settings = await service.GetAsync();
        settings.PrimaryLanguage.Code.Should().Be("en-US");
        settings.CommonTextOverrides.Should().ContainSingle(item => item.Key == "menu.about" && item.Values["zh-CN"] == "关于");
        (await service.GetBuildLocalizationOptionsAsync()).Text["menu.about"]["en-US"].Should().Be("About");
        var onDisk = await File.ReadAllTextAsync(workspace.Layout.Workspace.LocalizationFile);
        var site = await File.ReadAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile);
        await service.EnsureAsync();
        (await File.ReadAllTextAsync(workspace.Layout.Workspace.LocalizationFile)).Should().Be(onDisk);
        (await File.ReadAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile)).Should().Be(site);
    }

    /// <summary>坏文件不灌库、不覆盖，后台保存也拒绝。</summary>
    [Fact]
    public async Task EnsureAsync_BadFileLeavesDatabaseAndFileUntouched()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        db.LocalizationSettings.Add(new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = "zh-CN",
            EnabledLanguagesJson = """[{"code":"zh-CN","nativeName":"简体中文","englishName":"Simplified Chinese"}]""",
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = """{"common.readMore":{"zh-CN":"继续"}}""",
            UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
        });
        await db.SaveChangesAsync();
        const string broken = "[\n";
        await File.WriteAllTextAsync(workspace.Layout.Workspace.LocalizationFile, broken);

        await service.EnsureAsync();

        (await File.ReadAllTextAsync(workspace.Layout.Workspace.LocalizationFile)).Should().Be(broken);
        (await service.GetAsync()).PrimaryLanguage.Code.Should().Be("zh-CN");
        (await service.GetAsync()).CommonTextOverrides.Should().ContainSingle(item => item.Values["zh-CN"] == "继续");
        var save = await service.SaveAsync("en-US", ["en-US"], []);
        save.Succeeded.Should().BeFalse();
        save.Code.Should().Be(WorkspaceWriteResult.LocalizationInvalidCode);
        (await File.ReadAllTextAsync(workspace.Layout.Workspace.LocalizationFile)).Should().Be(broken);
        (await service.GetAsync()).PrimaryLanguage.Code.Should().Be("zh-CN");
    }

    /// <summary>保存往返保留 commonTextOverrides，并把主语言写回 site.yaml。</summary>
    [Fact]
    public async Task SaveAsync_RoundTripsOverridesAndSyncsSiteLanguage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        Directory.CreateDirectory(workspace.Layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile, """
            formatVersion: 1
            title: Demo
            language: zh-CN
            """);

        var saved = await service.SaveAsync("en-US", ["zh-CN", "en-US"], []);
        saved.Succeeded.Should().BeTrue();
        var text = await service.SaveCommonTextOverridesAsync(
        [
            new CommonI18nTextOverride
            {
                Key = "common.readMore",
                Values = new Dictionary<string, string> { ["en-US"] = "Read more", ["zh-CN"] = "继续阅读" },
            },
        ]);
        text.Succeeded.Should().BeTrue();

        var yaml = await File.ReadAllTextAsync(workspace.Layout.Workspace.LocalizationFile);
        yaml.Should().Contain("primaryLanguage: en-US");
        yaml.Should().Contain("common.readMore:");
        yaml.Should().Contain("Read more");
        yaml.Should().Contain("继续阅读");
        yaml.Should().NotContain("updatedAt");
        var site = await File.ReadAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile);
        site.Should().Contain("language: en-US");
        site.Should().Contain("title: Demo");
        var profile = await db.SiteProfileSettings.SingleAsync();
        profile.Language.Should().Be("en-US");
        var settings = await service.GetAsync();
        settings.CommonTextOverrides.Should().ContainSingle(item =>
            item.Key == "common.readMore" && item.Values["zh-CN"] == "继续阅读");
    }

    /// <summary>site.yaml 过新时，不导出本地化文件，也不改数据库。</summary>
    [Fact]
    public async Task EnsureAsync_RefusesWhenSiteFormatVersionIsTooNew()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        using var workspace = new TempWorkspace();
        var service = CreateService(db, workspace);
        Directory.CreateDirectory(workspace.Layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile, "formatVersion: 99\nlanguage: ja-JP\n");
        db.LocalizationSettings.Add(new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = "zh-CN",
            EnabledLanguagesJson = """[{"code":"zh-CN","nativeName":"简体中文","englishName":"Simplified Chinese"}]""",
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = "{}",
            UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
        });
        await db.SaveChangesAsync();

        await service.EnsureAsync();

        File.Exists(workspace.Layout.Workspace.LocalizationFile).Should().BeFalse();
        (await service.GetAsync()).PrimaryLanguage.Code.Should().Be("zh-CN");
        var save = await service.SaveAsync("en-US", ["en-US"], []);
        save.Code.Should().Be(WorkspaceFormatVersion.UnsupportedCode);
        (await service.GetAsync()).PrimaryLanguage.Code.Should().Be("zh-CN");
        (await File.ReadAllTextAsync(workspace.Layout.Workspace.SiteSettingsFile)).Should().Contain("formatVersion: 99");
    }

    private static LocalizationSettingsService CreateService(BocchiDbContext db, TempWorkspace workspace)
    {
        var profile = new SiteProfileSettingsService(db, workspace.Layout, TimeProvider.System, NullLogger<SiteProfileSettingsService>.Instance);
        return new LocalizationSettingsService(db, workspace.Layout, profile, TimeProvider.System, NullLogger<LocalizationSettingsService>.Instance);
    }

    private static async Task<BocchiDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>测试用的临时 DataRoot。</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "bocchi-loc-" + Guid.NewGuid().ToString("N"));
            Layout = new BocchiDataLayout(Root);
            Directory.CreateDirectory(Layout.Workspace.SiteDirectory);
        }

        public string Root { get; }

        public BocchiDataLayout Layout { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
