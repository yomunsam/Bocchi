using System.Reflection;

using Bocchi.Generator.Pipeline;
using Bocchi.Generator.Sinks;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;

using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Build;

/// <summary>
/// HomeServer 内对 <see cref="GeneratorPipeline"/> 的薄包装：负责注入版本号、构造默认 Sink，
/// 并保证同一时刻只能跑一个 Full 构建。Live 模式不串行化（每个 HTTP 请求独立 Sink）。
/// </summary>
public sealed class BuildOrchestrator : IDisposable
{
    private readonly GeneratorPipeline _pipeline;
    private readonly BocchiDataLayout _layout;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _fullBuildLock = new(1, 1);

    public BuildOrchestrator(GeneratorPipeline pipeline, BocchiDataLayout layout, IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _pipeline = pipeline;
        _layout = layout;
        _scopeFactory = scopeFactory;
    }

    public async Task<BuildResult> RunFullBuildAsync(BuildOptions options, string? themeId, CancellationToken cancellationToken)
    {
        await _fullBuildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sink = new FileSystemBuildSink(_layout);
            var enrichedOptions = await AddLocalizationSnapshotAsync(options, themeId, cancellationToken).ConfigureAwait(false);
            return await _pipeline.RunAsync(enrichedOptions, sink, themeId, ResolveBocchiVersion(), cancellationToken).ConfigureAwait(false);
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
        return RunLiveCoreAsync(options, themeId, sink, cancellationToken);
    }

    /// <summary>执行 Live build；单独拆出 async 方法是为了在调用 Generator 前补齐本地化快照。</summary>
    private async Task<BuildResult> RunLiveCoreAsync(BuildOptions options, string? themeId, IBuildSink sink, CancellationToken cancellationToken)
    {
        var enrichedOptions = await AddLocalizationSnapshotAsync(
            options with { Mode = BuildMode.Live, DisableUpToDateShortCircuit = true },
            themeId,
            cancellationToken).ConfigureAwait(false);
        return await _pipeline.RunAsync(enrichedOptions, sink, themeId, ResolveBocchiVersion(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>从 HomeServer 设置库读取站点本地化快照；Generator 本身保持对 EF Core 无感。</summary>
    private async Task<BuildOptions> AddLocalizationSnapshotAsync(BuildOptions options, string? themeId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var localizationSettings = scope.ServiceProvider.GetRequiredService<LocalizationSettingsService>();
        var themeSettings = scope.ServiceProvider.GetRequiredService<ThemeSettingsService>();
        var localization = await localizationSettings.GetBuildLocalizationOptionsAsync(cancellationToken).ConfigureAwait(false);
        var normalizedThemeId = string.IsNullOrWhiteSpace(themeId)
            ? (await themeSettings.GetDefaultAsync(cancellationToken).ConfigureAwait(false)).ThemeId
            : themeId.Trim();
        var themeTextOverrides = await themeSettings
            .GetBuildI18nTextOverridesAsync(normalizedThemeId, cancellationToken)
            .ConfigureAwait(false);
        return options with
        {
            Localization = localization with
            {
                ThemeTextOverrides = themeTextOverrides,
            },
        };
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
