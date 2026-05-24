using System.Text.Json;

using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>从磁盘读取 <c>theme.json</c>。</summary>
public static class ThemeManifestLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 在工作区 themes 目录中按 themeId 找 <c>theme.json</c>。返回 <c>null</c> 表示该 themeId 不存在。
    /// </summary>
    public static async Task<(ThemeManifest Manifest, string ThemeRoot)?> TryLoadAsync(
        string themesDirectory, string themeId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);

        var themesRoot = Path.GetFullPath(themesDirectory);
        var themeRoot = Path.GetFullPath(Path.Combine(themesRoot, themeId));
        EnsureUnderThemesRoot(themeRoot, themesRoot, themeId);
        return await TryLoadFromRootAsync(themeRoot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 从指定 Theme Root 读取 <c>theme.json</c>。它不限制路径来源，供 Dev Link 和 Package inspection 复用。
    /// </summary>
    public static async Task<(ThemeManifest Manifest, string ThemeRoot)?> TryLoadFromRootAsync(
        string themeRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeRoot);

        themeRoot = Path.GetFullPath(themeRoot);
        var manifestPath = Path.Combine(themeRoot, "theme.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        await using var stream = new FileStream(
            manifestPath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        var manifest = await JsonSerializer.DeserializeAsync<ThemeManifest>(stream, Options, cancellationToken).ConfigureAwait(false)
            ?? throw new ThemeRunnerException($"theme.json 反序列化为空：'{manifestPath}'");
        return (manifest, themeRoot);
    }

    private static void EnsureUnderThemesRoot(string themeRoot, string themesRoot, string themeId)
    {
        var rootWithSep = themesRoot.EndsWith(Path.DirectorySeparatorChar) ? themesRoot : themesRoot + Path.DirectorySeparatorChar;
        if (!themeRoot.StartsWith(rootWithSep, StringComparison.Ordinal) ||
            string.Equals(themeRoot, themesRoot, StringComparison.Ordinal))
        {
            throw new ThemeRunnerException($"Theme id '{themeId}' 未指向 themes 根目录下的独立 Theme。");
        }
    }
}
