using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>Resolver 与 Package inspection 共用的 Theme manifest 语义校验。</summary>
internal static class ThemeManifestValidator
{
    /// <summary>校验 Theme 私有 i18n key 必须严格位于当前 Theme id namespace 下。</summary>
    public static IEnumerable<ThemeDiagnostic> ValidatePrivateI18nNamespace(ThemeManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            yield break;
        }

        var prefix = $"theme.{manifest.Id.Trim()}.";
        foreach (var item in manifest.I18n?.Keys ?? [])
        {
            if (!item.Key.StartsWith(prefix, StringComparison.Ordinal) || item.Key.Length == prefix.Length)
            {
                yield return new ThemeDiagnostic(
                    ThemeDiagnosticSeverity.Error,
                    "theme-i18n-key-namespace-invalid",
                    $"Theme 私有 i18n key '{item.Key}' 必须使用 namespace '{prefix}'。");
            }
        }
    }
}
