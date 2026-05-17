using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Tests;

public sealed class LocalizationSettingsServiceTests
{
    [Fact]
    public async Task SaveAsync_EnsuresPrimaryLanguageIsEnabled()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var service = new LocalizationSettingsService(db, TimeProvider.System);

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
        var service = new LocalizationSettingsService(db, TimeProvider.System);

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
        var service = new LocalizationSettingsService(db, TimeProvider.System);

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

    private static async Task<BocchiDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<BocchiDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new BocchiDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}
