using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Build;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>发布流程使用的静态站点构建入口；隔离 UI 发布逻辑和 Generator 调用细节。</summary>
public interface IStaticSiteBuildRunner
{
    /// <summary>执行一次 production Full Build。</summary>
    /// <param name="forceRebuild">为 true 时跳过指纹短路，强制重新生成。</param>
    Task<BuildResult> RunPublishBuildAsync(CancellationToken cancellationToken, bool forceRebuild = false);
}

/// <summary>Home Server 生产实现：通过 <see cref="BuildOrchestrator"/> 生成 <c>output/public/</c>。</summary>
public sealed class StaticSiteBuildRunner : IStaticSiteBuildRunner
{
    private readonly BuildOrchestrator _orchestrator;

    /// <summary>构造静态站点构建入口。</summary>
    public StaticSiteBuildRunner(BuildOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public async Task<BuildResult> RunPublishBuildAsync(CancellationToken cancellationToken, bool forceRebuild = false)
    {
        var options = new BuildOptions
        {
            Mode = BuildMode.FullBuild,
            Environment = "production",
            IncludeDrafts = false,
            DisableUpToDateShortCircuit = forceRebuild,
        };
        return await _orchestrator.RunFullBuildAsync(options, themeId: null, cancellationToken).ConfigureAwait(false);
    }
}
