namespace Bocchi.GeneratorContract;

/// <summary>
/// Theme Contract 协议版本。对应 <c>Docs/Architecture.md §7</c>。
/// 接受规则为 <c>major.minor</c>：同 major 且主题 minor ≤ 程序当前 <see cref="Current"/> 的 minor。
/// </summary>
public static class ThemeContractVersion
{
    /// <summary>Theme Contract v1.0。</summary>
    public const string V1 = "1.0";

    /// <summary>当前 Generator 支持的最高 Theme Contract 版本（major.minor）。</summary>
    public const string Current = V1;

    /// <summary>
    /// 判断主题声明的 contractVersion 是否可被当前 Generator 接受。
    /// 非法格式、major 不一致、或主题 minor 高于 Current 时返回 <c>false</c>。
    /// </summary>
    public static bool IsCompatible(string? themeContractVersion)
    {
        if (!TryParse(themeContractVersion, out var themeMajor, out var themeMinor))
        {
            return false;
        }

        if (!TryParse(Current, out var currentMajor, out var currentMinor))
        {
            return false;
        }

        return themeMajor == currentMajor && themeMinor <= currentMinor;
    }

    /// <summary>解析 <c>major.minor</c>；允许可选的第三段 patch，但比较只看 major/minor。</summary>
    public static bool TryParse(string? value, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('.', StringSplitOptions.None);
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out major) ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out minor) ||
            major < 0 ||
            minor < 0)
        {
            return false;
        }

        if (parts.Length == 3 &&
            !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        return true;
    }
}
