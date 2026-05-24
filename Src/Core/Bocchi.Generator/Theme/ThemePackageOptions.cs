namespace Bocchi.Generator.Theme;

/// <summary>Theme Package 上传与 inspection 的保守限制，绑定到 <c>Bocchi:Themes:Packages</c>。</summary>
public sealed class ThemePackageOptions
{
    /// <summary>配置节路径。</summary>
    public const string SectionName = "Bocchi:Themes:Packages";

    /// <summary>允许上传的 zip 包最大字节数。</summary>
    public long MaxPackageBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>允许 zip 内包含的最大条目数量。</summary>
    public int MaxFileCount { get; set; } = 2000;

    /// <summary>允许单个 zip 条目的最大解压后字节数。</summary>
    public long MaxSingleFileBytes { get; set; } = 10 * 1024 * 1024;
}
