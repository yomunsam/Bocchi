using Microsoft.Extensions.DependencyInjection;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Tests;

/// <summary>后台时间按站点时区显示，并带短时区标签。</summary>
public sealed class DashboardTimeFormatterTests : IClassFixture<IsolatedDataRootWebApplicationFactory>
{
    private readonly IsolatedDataRootWebApplicationFactory _factory;

    public DashboardTimeFormatterTests(IsolatedDataRootWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(0, 0, "UTC")]
    [InlineData(8, 0, "UTC+8")]
    [InlineData(5, 30, "UTC+5:30")]
    [InlineData(-4, 0, "UTC-4")]
    public void FormatUtcOffset_MatchesFrontendStyle(int hours, int minutes, string expected)
    {
        var offset = new TimeSpan(hours, hours < 0 ? -minutes : minutes, 0);

        DashboardTimeFormatter.FormatUtcOffset(offset).Should().Be(expected);
    }

    [Fact]
    public void ResolveZone_InvalidId_FallsBackToServerLocal()
    {
        DashboardTimeFormatter.ResolveZone("Not/AZone").Should().Be(TimeZoneInfo.Local);
        DashboardTimeFormatter.ResolveZone(null).Should().Be(TimeZoneInfo.Local);
    }

    [Fact]
    public async Task FormatDateTime_UsesSiteTimeZoneAndLabel()
    {
        using var scope = _factory.Services.CreateScope();
        var siteProfile = scope.ServiceProvider.GetRequiredService<SiteProfileSettingsService>();
        var settings = await siteProfile.GetAsync(TestContext.Current.CancellationToken);
        settings.TimeZone = "Asia/Singapore";
        await scope.ServiceProvider.GetRequiredService<BocchiDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);

        var times = scope.ServiceProvider.GetRequiredService<DashboardTimeFormatter>();
        await times.LoadAsync(TestContext.Current.CancellationToken);

        // 下午 3 点在新加坡发布，库里是 07:14 UTC
        var startedAt = new DateTimeOffset(2026, 10, 6, 7, 14, 0, TimeSpan.Zero);
        times.FormatDateTime(startedAt).Should().Be("2026-10-06 15:14 (UTC+8)");
        times.FormatDateTime(startedAt.UtcDateTime).Should().Be("2026-10-06 15:14 (UTC+8)");
        times.FormatDate(new DateTimeOffset(2026, 10, 6, 20, 0, 0, TimeSpan.Zero)).Should().Be("2026-10-07");
    }
}
