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
