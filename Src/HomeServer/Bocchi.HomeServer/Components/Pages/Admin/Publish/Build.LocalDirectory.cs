using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Publishing;

namespace Bocchi.HomeServer.Components.Pages.Admin.Publish;

/// <summary>本地目录发布方案的表单状态与保存逻辑。</summary>
public partial class Build
{
    /// <summary>本地目录发布的目标绝对路径。</summary>
    private string _localTargetDirectory = string.Empty;

    /// <summary>是否允许覆盖没有 Bocchi 标记的非空目录。</summary>
    private bool _localAllowUnmarkedTarget;

    /// <summary>保存本地目录方案时禁用按钮。</summary>
    private bool _localSaving;

    /// <summary>当前是否在编辑本地目录渠道。</summary>
    private bool IsLocalDirectoryChannel
        => string.Equals(_publishChannel, PublishPlanService.LocalDirectoryChannel, StringComparison.Ordinal);

    /// <summary>当前是否在编辑 GitHub Pages 渠道。</summary>
    private bool IsGitHubPagesChannel
        => string.Equals(_publishChannel, PublishPlanService.GitHubPagesChannel, StringComparison.Ordinal);

    /// <summary>已保存的本地目录方案，供 Local Output 页列出。</summary>
    private IReadOnlyList<PublishPlanRecord> LocalDirectoryPlans
        => _publishPlans?
            .Where(plan => string.Equals(plan.Channel, PublishPlanService.LocalDirectoryChannel, StringComparison.Ordinal))
            .ToArray()
        ?? [];

    /// <summary>读取方案配置里的目标路径；坏 JSON 时回退为空。</summary>
    private static string LocalDirectoryPath(PublishPlanRecord plan)
    {
        try
        {
            return LocalDirectoryPublishConfiguration.FromJson(plan.ConfigurationJson).Normalize().TargetDirectory;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>渠道切换后清掉无关错误消息。</summary>
    private void OnPublishChannelChanged()
    {
        _githubMessage = null;
        _githubMessageIsDanger = false;
    }

    /// <summary>按当前渠道保存发布方案。</summary>
    private async Task SavePublishPlanAsync()
    {
        if (IsLocalDirectoryChannel)
        {
            await SaveLocalDirectoryPlanAsync();
            return;
        }

        await SaveGitHubPlanAsync();
    }

    /// <summary>保存本地目录发布方案；不需要凭据。</summary>
    private async Task SaveLocalDirectoryPlanAsync()
    {
        _githubMessageIsDanger = true;
        if (string.IsNullOrWhiteSpace(_planDisplayName))
        {
            _githubMessage = I18n["publish.addPlan.errorNameRequired"];
            return;
        }

        if (string.IsNullOrWhiteSpace(_localTargetDirectory))
        {
            _githubMessage = I18n["publish.localDirectory.errorPathRequired"];
            return;
        }

        if (!Path.IsPathRooted(_localTargetDirectory.Trim()))
        {
            _githubMessage = I18n["publish.localDirectory.errorPathAbsolute"];
            return;
        }

        _localSaving = true;
        try
        {
            var config = new LocalDirectoryPublishConfiguration
            {
                TargetDirectory = _localTargetDirectory,
                AllowUnmarkedTarget = _localAllowUnmarkedTarget,
            }.Normalize();
            var saved = await PublishPlans.SaveAsync(
                new PublishPlanSaveInput(
                    _editingPlanId,
                    _planDisplayName,
                    PublishPlanService.LocalDirectoryChannel,
                    config.ToJson(),
                    CredentialJson: null,
                    SetAsDefault: true),
                default);

            _editingPlanId = saved.Id;
            _localTargetDirectory = config.TargetDirectory;
            _publishChannel = PublishPlanService.LocalDirectoryChannel;
            _githubMessageIsDanger = false;
            _githubMessage = I18n["publish.addPlan.saved"];
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _githubMessageIsDanger = true;
            _githubMessage = ex.Message;
        }
        finally
        {
            _localSaving = false;
        }
    }

    /// <summary>从已有方案载入本地目录字段。</summary>
    private void LoadLocalDirectoryPlan(PublishPlanRecord plan)
    {
        var config = LocalDirectoryPublishConfiguration.FromJson(plan.ConfigurationJson).Normalize();
        _localTargetDirectory = config.TargetDirectory;
        _localAllowUnmarkedTarget = config.AllowUnmarkedTarget;
    }
}
