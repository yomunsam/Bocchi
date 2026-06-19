using Bocchi.GeneratorContract;

namespace Bocchi.Theme.FluidStatic;

/// <summary>默认静态 Theme renderer 的一次渲染请求。</summary>
public sealed record FluidStaticRenderRequest
{
    /// <summary>当前 Theme 实例根目录，例如 <c>&lt;data&gt;/themes/&lt;theme-id&gt;</c> 或 Dev Link 指向的 Theme Root。</summary>
    public required string ThemeRoot { get; init; }

    /// <summary>Theme Contract 输入目录，通常是 <c>&lt;workspace&gt;/../../cache/theme-input</c>。</summary>
    public required string InputDirectory { get; init; }

    /// <summary>Theme 本地输出目录。Generator 会在后续阶段收集这里的文件。</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>已加载的 Theme manifest，用于确认 id、版本和 runner 设置。</summary>
    public required ThemeManifest Manifest { get; init; }

    /// <summary>本次构建的站点基础 URL，主要用于 canonical 和元信息。</summary>
    public required string BaseUrl { get; init; }

    /// <summary>构建环境名称，例如 <c>production</c> 或 <c>development</c>。</summary>
    public required string Environment { get; init; }
}
