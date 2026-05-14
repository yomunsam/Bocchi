using Bocchi.GeneratorContract;

namespace Bocchi.Generator.Theme;

/// <summary>已加载的 Theme（manifest + 根目录）容器。引用类型以便走通 <see cref="Pipeline.BuildSession.GetItem{T}"/>。</summary>
public sealed record LoadedTheme(ThemeManifest Manifest, string ThemeRoot);