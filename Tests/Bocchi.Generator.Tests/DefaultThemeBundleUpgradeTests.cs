using System.Text;

using Bocchi.Themes.BuiltIn.Bundle;
using Bocchi.Workspace;

namespace Bocchi.Generator.Tests;

/// <summary>内置 Theme 按文件哈希升级：未改过的文件会被覆盖，改过的保留。</summary>
public sealed class DefaultThemeBundleUpgradeTests
{
    [Fact]
    public async Task EnsureAsync_OverwritesUntouchedFiles_AndKeepsEditedOnes()
    {
        var root = Path.Combine(Path.GetTempPath(), "bocchi-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layout = new BocchiDataLayout(root);
            Directory.CreateDirectory(layout.ThemesDirectory);

            var edited = await DefaultThemeBundle.EnsureAsync(layout.ThemesDirectory, TestContext.Current.CancellationToken);
            edited.Should().BeEmpty();

            var themeRoot = Path.Combine(layout.ThemesDirectory, DefaultThemeBundle.ThemeId);
            var appCss = Path.Combine(themeRoot, "assets", "app.css");
            File.Exists(appCss).Should().BeTrue();
            var original = await File.ReadAllTextAsync(appCss, TestContext.Current.CancellationToken);
            original.Length.Should().BeGreaterThan(0);

            // 模拟用户编辑
            await File.WriteAllTextAsync(appCss, "/* user edit */\n" + original, TestContext.Current.CancellationToken);

            // 再跑一次：改过的文件应保留，并出现在返回列表
            edited = await DefaultThemeBundle.EnsureAsync(layout.ThemesDirectory, TestContext.Current.CancellationToken);
            edited.Should().Contain("assets/app.css");
            (await File.ReadAllTextAsync(appCss, TestContext.Current.CancellationToken)).Should().StartWith("/* user edit */");

            // theme.json 未改过，版本应升到内置的 0.2.0
            var manifest = await File.ReadAllTextAsync(Path.Combine(themeRoot, "theme.json"), TestContext.Current.CancellationToken);
            manifest.Should().Contain("\"version\": \"0.2.0\"");
            File.Exists(DefaultThemeBundle.ResolveHashManifestPath(layout.ThemesDirectory)).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
