namespace Bocchi.Theme.DefaultStatic;

/// <summary>默认静态 Theme 的身份信息与运行实例物化入口。</summary>
public static class DefaultStaticThemeDefinition
{
    /// <summary>默认 Theme id。该值同时出现在 site.yaml、theme.json 和 Dashboard 配置中。</summary>
    public const string ThemeId = "default-static";

    /// <summary>默认 Theme 显示名称。</summary>
    public const string ThemeName = "Bocchi Mono";

    /// <summary>默认 Theme 初始版本。</summary>
    public const string ThemeVersion = "0.1.0";

    /// <summary>把随包分发的默认 Theme 源文件物化到 DataRoot themes 目录；已存在文件不会被覆盖。</summary>
    public static async Task EnsureAsync(string themesDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themesDirectory);

        var themeRoot = Path.Combine(themesDirectory, ThemeId);
        Directory.CreateDirectory(themeRoot);
        foreach (var relativePath in DefaultStaticThemeResources.Files)
        {
            await EnsureFileAsync(themeRoot, relativePath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>读取内置默认模板，供运行实例缺少新增模板文件时回退。</summary>
    internal static async Task<string?> TryReadTemplateAsync(string relativePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return await DefaultStaticThemeResources.TryReadTextAsync(
            $"templates/{relativePath.Replace('\\', '/')}",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>复制缺失的默认 Theme 文件；用户已经改过的运行实例始终保留。</summary>
    private static async Task EnsureFileAsync(string themeRoot, string relativePath, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(themeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(destination))
        {
            return;
        }

        await DefaultStaticThemeResources.CopyToFileAsync(relativePath, destination, cancellationToken)
            .ConfigureAwait(false);
    }
}
