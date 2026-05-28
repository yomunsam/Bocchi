using System.Globalization;

using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Publishing;

using Blazicons;

namespace Bocchi.HomeServer.Components.Pages.Admin.Publish;

/// <summary>发布中心页面的格式化辅助：状态映射、文案拼接和样式类。</summary>
public partial class Build
{
    /// <summary>最近一次生成摘要；没有历史时保持安静，不制造虚假的成功状态。</summary>
    private string LatestPublishSummary()
    {
        if (_lastResult is not null)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                I18n["publish.last.currentRunFormat"],
                BuildStatusText(_lastResult.Status),
                _lastResult.Duration.TotalSeconds.ToString("F1", CultureInfo.CurrentCulture));
        }

        var latest = _recent is { Count: > 0 } ? _recent[0] : null;
        if (latest is null)
        {
            return I18n["publish.last.none"];
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            I18n["publish.last.historyRunFormat"],
            BuildStatusText(latest.Status),
            latest.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
    }

    /// <summary>最近一次生成产物摘要；历史投影没有文件数量时展示稳定占位。</summary>
    private string LatestPublishArtifactSummary()
    {
        if (_lastResult is not null)
        {
            return string.Format(CultureInfo.CurrentCulture, I18n["publish.result.artifactCountFormat"], _lastResult.Artifacts.Count);
        }

        return I18n["publish.last.artifactsPending"];
    }

    /// <summary>把构建状态映射到柔和状态色。</summary>
    private static string BuildStatusTone(BuildStatus status)
        => status switch
        {
            BuildStatus.Succeeded => "success",
            BuildStatus.Skipped => "info",
            _ => "danger",
        };

    /// <summary>把构建状态映射为本地化文案。</summary>
    private string BuildStatusText(BuildStatus status)
        => status switch
        {
            BuildStatus.Succeeded => I18n["publish.status.succeeded"],
            BuildStatus.Skipped => I18n["publish.status.skipped"],
            _ => I18n["publish.status.failed"],
        };

    /// <summary>一键发布目标标签颜色。</summary>
    private string OneClickTargetTone() => DefaultPublishPlan is null ? "neutral" : "success";

    /// <summary>一键发布目标文案；没有方案时明确说明会落到本地静态输出。</summary>
    private string OneClickTargetText()
    {
        var plan = DefaultPublishPlan;
        return plan is null
            ? I18n["publish.oneClick.targetLocal"]
            : string.Format(CultureInfo.CurrentCulture, I18n["publish.oneClick.targetFormat"], plan.DisplayName);
    }

    /// <summary>一键发布结果消息 class。</summary>
    private string OneClickMessageClass()
        => _oneClickMessageIsDanger
            ? "bocchi-publish-remote-message bocchi-publish-remote-message--danger"
            : "bocchi-publish-remote-message";

    /// <summary>GitHub Pages 面板消息 class。</summary>
    private string GitHubMessageClass()
        => _githubMessageIsDanger
            ? "bocchi-publish-remote-message bocchi-publish-remote-message--danger"
            : "bocchi-publish-remote-message";

    /// <summary>最新本地生成时间。</summary>
    private string LatestBuildTimeText()
    {
        if (_lastResult is not null)
        {
            return _lastResult.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        }

        var latest = _recent is { Count: > 0 } ? _recent[0] : null;
        return latest is null
            ? I18n["publish.statusCard.never"]
            : latest.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>最新远端发布时间。</summary>
    private string LatestRemotePublishText()
    {
        var latest = _publishRuns is { Count: > 0 } ? _publishRuns[0] : null;
        return latest is null
            ? I18n["publish.statusCard.never"]
            : latest.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>默认发布方案名称。</summary>
    private string DefaultPlanStatusText()
        => DefaultPublishPlan?.DisplayName ?? I18n["publish.statusCard.none"];

    /// <summary>发布方案数量摘要。</summary>
    private string PublishPlanCountText()
        => string.Format(CultureInfo.CurrentCulture, I18n["publish.statusCard.planCountFormat"], _publishPlans?.Count ?? 0);

    /// <summary>发布渠道显示名。</summary>
    private string ChannelDisplayName(string channel)
        => channel switch
        {
            PublishPlanService.StaticFilesChannel => I18n["publish.channel.staticFiles"],
            PublishPlanService.LocalDirectoryChannel => I18n["publish.channel.localDirectory"],
            PublishPlanService.GitHubPagesChannel => I18n["publish.channel.githubPages"],
            PublishPlanService.CloudflarePagesChannel => I18n["publish.channel.cloudflarePages"],
            PublishPlanService.CustomChannel => I18n["publish.channel.custom"],
            _ => channel,
        };

    /// <summary>发布渠道图标。</summary>
    private static SvgIcon PlanChannelIcon(string channel)
        => channel switch
        {
            PublishPlanService.GitHubPagesChannel => Lucide.GitBranch,
            PublishPlanService.CustomChannel => Lucide.Settings,
            _ => Lucide.FolderOpen,
        };

    /// <summary>发布方案编辑入口；当前只有 GitHub Pages 方案拥有配置页。</summary>
    private static string? PlanEditUrl(PublishPlanRecord plan)
        => string.Equals(plan.Channel, PublishPlanService.GitHubPagesChannel, StringComparison.Ordinal)
            ? $"/Admin/Publish/AddPlan/{plan.Id}"
            : null;

    /// <summary>把发布状态映射为柔和状态色。</summary>
    private static string PublishStatusTone(PublishRunStatus status)
        => status switch
        {
            PublishRunStatus.Succeeded => "success",
            PublishRunStatus.Running => "info",
            _ => "danger",
        };

    /// <summary>把发布状态映射为本地化文案。</summary>
    private string PublishStatusText(PublishRunStatus status)
        => status switch
        {
            PublishRunStatus.Succeeded => I18n["publish.status.succeeded"],
            PublishRunStatus.Running => I18n["publish.status.running"],
            _ => I18n["publish.status.failed"],
        };
}
