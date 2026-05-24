using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Tests;

/// <summary>验证发布方案的默认方案规则、配置 JSON 规范化和凭据保护。</summary>
public sealed class PublishPlanServiceTests
{
    /// <summary>第一条发布方案会自动成为默认方案，支撑发布页的一键发布入口。</summary>
    [Fact]
    public async Task SaveAsync_MakesFirstPlanDefault()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var service = CreateService(db);

        var first = await service.SaveAsync(new PublishPlanSaveInput(
            Id: null,
            DisplayName: "Local static",
            Channel: PublishPlanService.StaticFilesChannel,
            ConfigurationJson: "{}",
            CredentialJson: null,
            SetAsDefault: false));
        var second = await service.SaveAsync(new PublishPlanSaveInput(
            Id: null,
            DisplayName: "GitHub Pages",
            Channel: PublishPlanService.GitHubPagesChannel,
            ConfigurationJson: "{}",
            CredentialJson: null,
            SetAsDefault: false));

        first.IsDefault.Should().BeTrue();
        second.IsDefault.Should().BeFalse();
        var plans = await service.ListAsync();
        plans.Should().ContainSingle(x => x.IsDefault);
    }

    /// <summary>切换默认方案时清理旧默认，避免发布页出现多个一键发布目标。</summary>
    [Fact]
    public async Task SetDefaultAsync_KeepsOnlyOneDefaultPlan()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var service = CreateService(db);

        var first = await service.SaveAsync(new PublishPlanSaveInput(null, "Local static", PublishPlanService.StaticFilesChannel, "{}", null, false));
        var second = await service.SaveAsync(new PublishPlanSaveInput(null, "GitHub Pages", PublishPlanService.GitHubPagesChannel, "{}", null, false));

        await service.SetDefaultAsync(second.Id);

        var plans = await service.ListAsync();
        plans.Should().ContainSingle(x => x.IsDefault);
        plans.Single(x => x.IsDefault).Id.Should().Be(second.Id);
        plans.Single(x => x.Id == first.Id).IsDefault.Should().BeFalse();
    }

    /// <summary>非敏感配置保存为规范 JSON；敏感凭据必须经过 Data Protection，不落明文。</summary>
    [Fact]
    public async Task SaveAsync_NormalizesConfigurationAndProtectsCredentials()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDbContextAsync(connection);
        var service = CreateService(db);

        var saved = await service.SaveAsync(new PublishPlanSaveInput(
            Id: null,
            DisplayName: "GitHub Pages",
            Channel: PublishPlanService.GitHubPagesChannel,
            ConfigurationJson: """{ "repo": "yomu/site", "branch": "gh-pages" }""",
            CredentialJson: """{ "token": "plain-secret" }""",
            SetAsDefault: true));

        saved.ConfigurationJson.Should().Be("""{"repo":"yomu/site","branch":"gh-pages"}""");
        saved.ProtectedCredentialJson.Should().NotBeNullOrWhiteSpace();
        saved.ProtectedCredentialJson.Should().NotContain("plain-secret");
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

    private static PublishPlanService CreateService(BocchiDbContext db)
    {
        var keyDir = Path.Combine(Path.GetTempPath(), "bocchi-test-keys", Guid.NewGuid().ToString("N"));
        var protection = DataProtectionProvider.Create(new DirectoryInfo(keyDir));
        return new PublishPlanService(db, protection, TimeProvider.System);
    }
}
