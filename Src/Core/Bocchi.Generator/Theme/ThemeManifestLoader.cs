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

        var themeRoot = Path.Combine(themesDirectory, themeId);
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
}
