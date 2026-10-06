using Microsoft.Extensions.DependencyInjection;
using System.Net;

using Bocchi.HomeServer.Components.Ui;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Tests;

/// <summary>后台配色选择：Settings 总览上的三个色块，以及首屏前写入配色的 head 脚本。</summary>
public sealed class DashboardPaletteTests : IClassFixture<IsolatedDataRootWebApplicationFactory>
{
    private readonly IsolatedDataRootWebApplicationFactory _factory;

    public DashboardPaletteTests(IsolatedDataRootWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SettingsHub_RendersPaletteRadioGroup()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Settings");

        response.EnsureSuccessStatusCode();
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        body.Should().Contain("role=\"radiogroup\"");
        body.Should().Contain("Admin palette");
        foreach (var (palette, label) in new[] { ("tracksuit", "Tracksuit"), ("maid", "Maid"), ("clip", "Hair clip") })
        {
            body.Should().MatchRegex(
                $"<input type=\"radio\" name=\"bocchi-dashboard-palette\" value=\"{palette}\" aria-label=\"{label}\" data-bocchi-palette-option=\"{palette}\"");
        }

        // 选中态交给首屏脚本和 CSS，服务端不输出 checked，避免和浏览器里存的配色打架
        body.Should().NotContain("data-bocchi-palette-option=\"tracksuit\" checked");
    }

    [Fact]
    public async Task DashboardHead_SetsPaletteBeforeStylesheets()
    {
        using var client = await _factory.CreateAdminClientAsync();

        var response = await client.GetAsync("/Admin/Settings");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        var paletteScript = head.IndexOf("localStorage.getItem(\"bocchi.dashboard.palette\")", StringComparison.Ordinal);
        var stylesheet = head.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);

        // 内联同步脚本排在样式表前面，浏览器画第一帧之前 <html> 上已经有 data-bocchi-palette
        paletteScript.Should().BePositive();
        stylesheet.Should().BeGreaterThan(paletteScript);
        head[..paletteScript].LastIndexOf("<script", StringComparison.Ordinal)
            .Should().Be(head[..paletteScript].LastIndexOf("<script>", StringComparison.Ordinal));
    }

    [Fact]
    public void PaletteLabels_ExistInChineseAndEnglish()
    {
        using var scope = _factory.Services.CreateScope();
        var i18n = scope.ServiceProvider.GetRequiredService<DashboardLocalizationService>();

        BocchiPalettePicker.Palettes.Should().Equal("tracksuit", "maid", "clip");
        i18n.Get("settings.palette.title", "zh-CN").Should().Be("后台配色");
        i18n.Get("settings.palette.tracksuit", "zh-CN").Should().Be("运动服");
        i18n.Get("settings.palette.maid", "zh-CN").Should().Be("女仆");
        i18n.Get("settings.palette.clip", "zh-CN").Should().Be("发夹");
        foreach (var palette in BocchiPalettePicker.Palettes)
        {
            i18n.Get($"settings.palette.{palette}", "en-US").Should().NotBe($"settings.palette.{palette}");
        }
    }
}
