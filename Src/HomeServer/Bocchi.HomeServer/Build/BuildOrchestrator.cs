using System.Reflection;

using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.Workspace;

namespace Bocchi.HomeServer.Build;

/// <summary>
/// HomeServer 内对 <see cref="GeneratorPipeline"/> 的薄包装：负责注入版本号、构造默认 Sink，
/// 并保证同一时刻只能跑一个 Full 构建。Live 模式不串行化（每个 HTTP 请求独立 Sink）。
/// </summary>
public sealed class BuildOrchestrator : IDisposable
{
    private readonly GeneratorPipeline _pipeline;
    private readonly WorkspaceLayout _layout;
    private readonly SemaphoreSlim _fullBuildLock = new(1, 1);

    public BuildOrchestrator(GeneratorPipeline pipeline, WorkspaceLayout layout)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(layout);
        _pipeline = pipeline;
        _layout = layout;
    }

    public async Task<BuildResult> RunFullBuildAsync(BuildOptions options, string? themeId, CancellationToken cancellationToken)
    {
        await _fullBuildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sink = new FileSystemBuildSink(_layout);
            return await _pipeline.RunAsync(options, sink, themeId, ResolveBocchiVersion(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fullBuildLock.Release();
        }
    }

    public Task<BuildResult> RunLiveAsync(BuildOptions options, string? themeId, IBuildSink sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);
        // Live 模式不串行化：HTTP 并发请求可以并行
        return _pipeline.RunAsync(
            options with { Mode = BuildMode.Live, DisableUpToDateShortCircuit = true },
            sink, themeId, ResolveBocchiVersion(), cancellationToken);
    }

    /// <summary>读取 Bocchi.HomeServer 程序集的 InformationalVersion。</summary>
    public static string ResolveBocchiVersion()
    {
        var asm = typeof(BuildOrchestrator).Assembly;
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "0.0.0-dev";
    }

    public void Dispose() => _fullBuildLock.Dispose();
}