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
        await using var harness = await CreateHarnessAsync();
        var service = harness.Localization;

        await service.SaveAsync("ja-JP", ["en-US"], []);

        var settings = await service.GetAsync();
        settings.PrimaryLanguage.Code.Should().Be("ja-JP");
        settings.EnabledLanguages.Select(x => x.Code).Should().Contain(["en-US", "ja-JP"]);
        settings.UrlPolicy.Should().Be(LocalizationSettingsService.PrimaryUnprefixedUrlPolicy);

        var yaml = await File.ReadAllTextAsync(harness.Layout.Workspace.LocalizationFile);
        yaml.Should().Contain("primaryLanguage: ja-JP");
        var siteYaml = await File.ReadAllTextAsync(harness.Layout.Workspace.SiteSettingsFile);
        siteYaml.Should().Contain("language: ja-JP");
    }

    [Fact]
    public async Task SaveAsync_PreservesCustomLanguagesInPicklist()
    {
        await using var harness = await CreateHarnessAsync();
        var service = harness.Localization;

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
        await using var harness = await CreateHarnessAsync();
        var service = harness.Localization;

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

        var yaml = await File.ReadAllTextAsync(harness.Layout.Workspace.LocalizationFile);
        yaml.Should().Contain("commonTextOverrides:");
        yaml.Should().Contain("menu.home:");
    }

    [Fact]
    public async Task EnsureAsync_NoFile_ExportsDbToWorkspace()
    {
        await using var harness = await CreateHarnessAsync();
        var primary = LocalizationSettingsService.BuiltInLanguages.First(x => x.Code == "en-US");
        harness.Db.LocalizationSettings.Add(new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = "en-US",
            EnabledLanguagesJson = System.Text.Json.JsonSerializer.Serialize(new[] { primary }),
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = """{"menu.about":{"en-US":"About"}}""",
            UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Db.SaveChangesAsync();

        File.Exists(harness.Layout.Workspace.LocalizationFile).Should().BeFalse();
        await harness.Localization.EnsureAsync();

        File.Exists(harness.Layout.Workspace.LocalizationFile).Should().BeTrue();
        var yaml = await File.ReadAllTextAsync(harness.Layout.Workspace.LocalizationFile);
        yaml.Should().Contain("primaryLanguage: en-US");
        yaml.Should().Contain("menu.about:");
        yaml.Should().Contain("en-US: About");
    }

    [Fact]
    public async Task EnsureAsync_FileImportsIntoDb()
    {
        await using var harness = await CreateHarnessAsync();
        await File.WriteAllTextAsync(harness.Layout.Workspace.LocalizationFile, """
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

        await harness.Localization.EnsureAsync();

        var settings = await harness.Localization.GetAsync();
        settings.PrimaryLanguage.Code.Should().Be("en-US");
        settings.EnabledLanguages.Select(x => x.Code).Should().Contain(["en-US", "zh-CN"]);
        settings.CommonTextOverrides.Should().ContainSingle(x => x.Key == "menu.about");
        settings.CommonTextOverrides[0].Values["zh-CN"].Should().Be("关于");

        var siteYaml = await File.ReadAllTextAsync(harness.Layout.Workspace.SiteSettingsFile);
        siteYaml.Should().Contain("language: en-US");
    }

    [Fact]
    public async Task EnsureAsync_BadFile_DoesNotImportOrOverwrite()
    {
        await using var harness = await CreateHarnessAsync();
        const string bad = "primaryLanguage: [\n";
        await File.WriteAllTextAsync(harness.Layout.Workspace.LocalizationFile, bad);

        var primary = LocalizationSettingsService.BuiltInLanguages.First(x => x.Code == "zh-CN");
        harness.Db.LocalizationSettings.Add(new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = "zh-CN",
            EnabledLanguagesJson = System.Text.Json.JsonSerializer.Serialize(new[] { primary }),
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = "{}",
            UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Db.SaveChangesAsync();

        await harness.Localization.EnsureAsync();

        (await File.ReadAllTextAsync(harness.Layout.Workspace.LocalizationFile)).Should().Be(bad);
        var settings = await harness.Localization.GetAsync();
        settings.PrimaryLanguage.Code.Should().Be("zh-CN");
    }

    [Fact]
    public async Task EnsureAsync_PrimaryMissingFromEnabled_WritesBackFile()
    {
        await using var harness = await CreateHarnessAsync();
        await File.WriteAllTextAsync(harness.Layout.Workspace.LocalizationFile, """
            primaryLanguage: ja-JP
            urlPolicy: PrimaryUnprefixed
            enabledLanguages:
              - code: en-US
                nativeName: English
                englishName: English
            customLanguages: []
            commonTextOverrides: {}
            """);

        await harness.Localization.EnsureAsync();

        var yaml = await File.ReadAllTextAsync(harness.Layout.Workspace.LocalizationFile);
        yaml.Should().Contain("primaryLanguage: ja-JP");
        yaml.Should().Contain("code: ja-JP");

        var settings = await harness.Localization.GetAsync();
        settings.EnabledLanguages.Select(x => x.Code).Should().Contain(["en-US", "ja-JP"]);
    }

    [Fact]
    public async Task GetBuildLocalizationOptionsAsync_BadFile_Refuses()
    {
        await using var harness = await CreateHarnessAsync();
        await File.WriteAllTextAsync(harness.Layout.Workspace.LocalizationFile, "primaryLanguage: [\n");

        var primary = LocalizationSettingsService.BuiltInLanguages.First(x => x.Code == "zh-CN");
        harness.Db.LocalizationSettings.Add(new LocalizationSettingsRecord
        {
            Id = 1,
            PrimaryLanguage = "zh-CN",
            EnabledLanguagesJson = System.Text.Json.JsonSerializer.Serialize(new[] { primary }),
            CustomLanguagesJson = "[]",
            CommonTextOverridesJson = "{}",
            UrlPolicy = LocalizationSettingsService.PrimaryUnprefixedUrlPolicy,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await harness.Db.SaveChangesAsync();

        var act = async () => await harness.Localization.GetBuildLocalizationOptionsAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*localization.yaml*拒绝构建*");
    }

    [Fact]
    public async Task SaveAsync_RoundTripsCommonTextOverrides()
    {
        await using var harness = await CreateHarnessAsync();
        await harness.Localization.SaveAsync("zh-CN", ["zh-CN", "en-US"], []);
        await harness.Localization.SaveCommonTextOverridesAsync(
        [
            new CommonI18nTextOverride
            {
                Key = "common.readMore",
                Values = new Dictionary<string, string>
                {
                    ["zh-CN"] = "阅读更多",
                    ["en-US"] = "Read more",
                },
            },
        ]);

        harness.Db.ChangeTracker.Clear();
        await harness.Localization.EnsureAsync();
        var settings = await harness.Localization.GetAsync();
        settings.CommonTextOverrides.Should().ContainSingle(x => x.Key == "common.readMore");
        settings.CommonTextOverrides[0].Values["en-US"].Should().Be("Read more");
    }

    private static async Task<Harness> CreateHarnessAsync()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "bocchi-loc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        var layout = new BocchiDataLayout(dataRoot);
        Directory.CreateDirectory(layout.Workspace.SiteDirectory);
        await File.WriteAllTextAsync(
            layout.Workspace.SiteSettingsFile,
            "formatVersion: 1\ntitle: Test\nlanguage: zh-CN\ndefaultThemeId: bocchi-mono\n");

        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var siteProfile = new SiteProfileSettingsService(
            db, layout, TimeProvider.System, NullLogger<SiteProfileSettingsService>.Instance);
        var localization = new LocalizationSettingsService(
            db, layout, siteProfile, TimeProvider.System, NullLogger<LocalizationSettingsService>.Instance);
        return new Harness(dataRoot, layout, connection, db, localization);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(
            string dataRoot,
            BocchiDataLayout layout,
            SqliteConnection connection,
            BocchiDbContext db,
            LocalizationSettingsService localization)
        {
            DataRoot = dataRoot;
            Layout = layout;
            Connection = connection;
            Db = db;
            Localization = localization;
        }

        public string DataRoot { get; }
        public BocchiDataLayout Layout { get; }
        public SqliteConnection Connection { get; }
        public BocchiDbContext Db { get; }
        public LocalizationSettingsService Localization { get; }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
            try
            {
                Directory.Delete(DataRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
