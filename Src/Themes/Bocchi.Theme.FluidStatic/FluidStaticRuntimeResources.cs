using System.Reflection;

namespace Bocchi.Theme.FluidStatic;

/// <summary>访问并输出 Fluid Static v1 的浏览器端公共 runtime。</summary>
internal static class FluidStaticRuntimeResources
{
    /// <summary>runtime 在程序集中的固定 resource 名称。</summary>
    private const string ResourceName = "Bocchi.Theme.FluidStatic.Runtime.fluid-static-v1.js";

    /// <summary>把公共 runtime 写入本次 Theme build 输出。</summary>
    public static async Task CopyToAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var destination = Path.Combine(outputDirectory, "_bocchi", "fluid-static-v1.js");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var source = typeof(FluidStaticRuntimeResources).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new FluidStaticException($"Fluid Static runtime embedded resource 缺失：'{ResourceName}'。");
        await using var target = new FileStream(
            destination,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }
}
