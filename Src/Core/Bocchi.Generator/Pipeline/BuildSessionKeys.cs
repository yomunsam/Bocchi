namespace Bocchi.Generator.Pipeline;

/// <summary><see cref="BuildSession"/> 任意键值上下文的标准 key。</summary>
public static class BuildSessionKeys
{
    /// <summary>当前 Theme id（字符串）。</summary>
    public const string ThemeId = "themeId";

    /// <summary>Bocchi 自身版本号（字符串）。</summary>
    public const string BocchiVersion = "bocchiVersion";

    /// <summary>已解析的 Theme 根目录 + manifest（<see cref="Theme.ThemeRunInvocation"/> 的源材料）。</summary>
    public const string LoadedTheme = "loadedTheme";
}
