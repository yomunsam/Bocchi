using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>已加载的 Theme（manifest + 根目录）容器。引用类型以便走通 <see cref="Pipeline.BuildSession.GetItem{T}"/>。</summary>
public sealed record LoadedTheme(ThemeManifest Manifest, string ThemeRoot)
{
    /// <summary>Theme 来源，供后续阶段和 Build log 说明当前实际使用的 Theme Root。</summary>
    public ThemeSourceKind SourceKind { get; init; } = ThemeSourceKind.Installed;
}
