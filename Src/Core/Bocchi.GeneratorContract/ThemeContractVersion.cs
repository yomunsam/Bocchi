namespace Bocchi.GeneratorContract;

/// <summary>
/// Theme Contract 协议版本。对应 <c>Docs/Architecture.md §7</c>。
/// </summary>
public static class ThemeContractVersion
{
    /// <summary>Theme Contract v1。</summary>
    public const string V1 = "1.0";

    /// <summary>当前 Home Server 支持的 Theme Contract 版本。</summary>
    public const string Current = V1;
}
