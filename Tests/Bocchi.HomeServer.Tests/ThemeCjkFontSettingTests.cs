using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

using Bocchi.GeneratorContract;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

namespace Bocchi.HomeServer.Tests;

/// <summary>bocchi-mono 的中文网页字体样式表设置：只收完整 https 网址，标题按 Dashboard 语言显示。</summary>
public sealed class ThemeCjkFontSettingTests : IClassFixture<IsolatedDataRootWebApplicationFactory>
{
    private const string FieldKey = "visual.cjkFontStylesheetUrl";
    private readonly IsolatedDataRootWebApplicationFactory _factory;

    public ThemeCjkFontSettingTests(IsolatedDataRootWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/css,body{}")]
    [InlineData("http://fonts.example/cjk.css")]
    [InlineData("/assets/cjk.css")]
    [InlineData("fonts.example/cjk.css")]
    [InlineData("https://fonts.example/a b.css")]
    public async Task SaveCustomization_RejectsNonHttpsUrls(string value)
    {
        using var scope = await CreateScopeAsync();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();

        var save = () => settings.SaveCustomizationAsync("bocchi-mono", [new ThemeConfigValueInput { Key = FieldKey, Value = value }]);

        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*https://*");
    }

    [Fact]
    public async Task SaveCustomization_StoresHttpsUrlAndClearsOnEmpty()
    {
        using var scope = await CreateScopeAsync();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();
        const string url = "https://fonts.googleapis.com/css2?family=Noto+Serif+SC:wght@500&display=swap";

        await settings.SaveCustomizationAsync("bocchi-mono", [new ThemeConfigValueInput { Key = FieldKey, Value = "  " + url + " " }]);
        (await ReadVisualAsync(scope)).GetProperty("cjkFontStylesheetUrl").GetString().Should().Be(url);

        await settings.SaveCustomizationAsync("bocchi-mono", [new ThemeConfigValueInput { Key = FieldKey, Value = "" }]);
        (await ReadVisualAsync(scope)).TryGetProperty("cjkFontStylesheetUrl", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetCustomization_ResolvesFieldLabelFromThemeText()
    {
        using var scope = await CreateScopeAsync();
        var settings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();

        var view = await settings.GetCustomizationAsync("bocchi-mono", TestContext.Current.CancellationToken);

        var field = view.Groups.SelectMany(group => group.Fields).Single(field => field.Key == FieldKey);
        field.Type.Should().Be(ThemeConfigFieldType.Url);
        field.Title.Should().NotStartWith("i18n://");
        field.Description.Should().NotStartWith("i18n://");
        field.Placeholder.Should().Contain("display=swap");
        field.TextValue.Should().BeEmpty();
    }

    private async Task<IServiceScope> CreateScopeAsync()
    {
        using (await _factory.CreateAdminClientAsync())
        {
        }

        return _factory.Services.CreateScope();
    }

    private static async Task<JsonElement> ReadVisualAsync(IServiceScope scope)
    {
        var layout = scope.ServiceProvider.GetRequiredService<BocchiDataLayout>();
        var json = await File.ReadAllTextAsync(Path.Combine(layout.ThemeConfigDirectory, "bocchi-mono.json"));
        return JsonDocument.Parse(json).RootElement.GetProperty("visual").Clone();
    }
}
