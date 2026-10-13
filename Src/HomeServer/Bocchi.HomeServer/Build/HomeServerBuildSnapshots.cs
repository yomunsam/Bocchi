using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Build;

/// <summary>
/// 把分类树和前台语言投影放进构建参数。Admin 构建和 CLI 共用这一步，
/// 这样坏的 <c>categories.yaml</c> / <c>localization.yaml</c> 会在进 Generator 之前失败。
/// </summary>
internal static class HomeServerBuildSnapshots
{
    /// <summary>
    /// 读取快照并写回 <paramref name="options"/>。文件不可用时抛出 <see cref="WorkspaceSnapshotException"/>。
    /// </summary>
    public static async Task<BuildOptions> ApplyAsync(
        IServiceProvider services,
        BuildOptions options,
        string? themeId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var localizationSettings = services.GetRequiredService<LocalizationSettingsService>();
        var siteProfileSettings = services.GetRequiredService<SiteProfileSettingsService>();
        var themeSettings = services.GetRequiredService<ThemeSettingsService>();
        var categoryTree = services.GetRequiredService<CategoryTreeService>();
        var localization = await localizationSettings.GetBuildLocalizationOptionsAsync(cancellationToken).ConfigureAwait(false);
        var postCategories = await categoryTree.GetBuildPostCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var normalizedThemeId = string.IsNullOrWhiteSpace(themeId)
            ? (await siteProfileSettings.GetAsync(cancellationToken).ConfigureAwait(false)).DefaultThemeId
            : themeId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedThemeId))
        {
            normalizedThemeId = (await themeSettings.GetDefaultAsync(cancellationToken).ConfigureAwait(false)).ThemeId;
        }

        var themeTextOverrides = await themeSettings
            .GetBuildI18nTextOverridesAsync(normalizedThemeId, cancellationToken)
            .ConfigureAwait(false);
        return options with
        {
            Localization = localization with
            {
                ThemeTextOverrides = themeTextOverrides,
            },
            PostCategories = postCategories,
        };
    }
}