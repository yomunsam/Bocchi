using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Build;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Components.Pages.Admin.Publish;

/// <summary>发布中心页面的用户操作：一键发布和本地构建。</summary>
public partial class Build
{
    /// <summary>执行首页一键发布；存在默认方案时走远端发布，否则只生成本地静态文件。</summary>
    private async Task PublishOneClickAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _oneClickMessage = null;
        _oneClickMessageIsDanger = false;
        try
        {
            var plan = DefaultPublishPlan;
            if (plan is null)
            {
                await RunBuildOnlyAsync();
                _oneClickMessageIsDanger = _lastResult?.Status == BuildStatus.Failed;
                _oneClickMessage = _oneClickMessageIsDanger
                    ? _lastResult?.Reason ?? I18n["publish.oneClick.localFailed"]
                    : I18n["publish.oneClick.localSucceeded"];
                return;
            }

            var result = await PublishExecution.PublishAsync(plan.Id, default);
            _oneClickMessageIsDanger = result.Status == PublishRunStatus.Failed;
            _oneClickMessage = _oneClickMessageIsDanger
                ? result.ErrorMessage ?? I18n["publish.github.publishFailed"]
                : I18n["publish.oneClick.remoteSucceeded"];
            await RefreshAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>执行本地静态输出生成；一键发布没有发布方案时复用这个最小路径。</summary>
    private async Task RunBuildOnlyAsync()
    {
        var options = new BuildOptions
        {
            Mode = BuildMode.FullBuild,
            Environment = "production",
            IncludeDrafts = false,
        };
        _lastResult = await Orchestrator.RunFullBuildAsync(
            options,
            themeId: null,
            default);
        await RefreshAsync();
    }
}
