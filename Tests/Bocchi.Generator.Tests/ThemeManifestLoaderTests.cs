using Bocchi.Generator.Theme;

namespace Bocchi.Generator.Tests;

public sealed class ThemeManifestLoaderTests
{
    [Fact]
    public async Task TryLoadAsync_RejectsThemeIdOutsideThemesRoot()
    {
        var temp = Path.Combine(Path.GetTempPath(), "bocchi-theme-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var themesRoot = Path.Combine(temp, "themes");
            Directory.CreateDirectory(themesRoot);

            var act = async () => await ThemeManifestLoader.TryLoadAsync(themesRoot, "../escape", default);

            await act.Should().ThrowAsync<ThemeRunnerException>();
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }
}
