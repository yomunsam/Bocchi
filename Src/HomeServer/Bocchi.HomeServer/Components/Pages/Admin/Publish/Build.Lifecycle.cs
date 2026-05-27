using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Publishing;

namespace Bocchi.HomeServer.Components.Pages.Admin.Publish;

/// <summary>发布中心页面的生命周期：初始化和数据加载。</summary>
public partial class Build
{
    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        await LoadGitHubPublishAuthSettingsAsync();
        await LoadGitConnectionsAsync();
        await LoadPublishPlanEditorAsync();
    }

    /// <summary>读取近期生成记录和发布运行记录。</summary>
    private async Task RefreshAsync()
    {
        _recent = await Store.ListRecentRunsAsync(20, default);
        _publishRuns = await PublishExecution.ListRecentRunsAsync(20, default);
        _publishPlans = await PublishPlans.ListAsync(default);
    }

    /// <summary>读取 Git provider 连接，用于发布向导复用已有授权。</summary>
    private async Task LoadGitConnectionsAsync()
    {
        _gitConnections = await GitConnections.ListAsync(default);
        var githubConnections = GitHubConnections.ToArray();
        if (_selectedGitConnectionId is null || !githubConnections.Any(x => x.Id == _selectedGitConnectionId.Value))
        {
            _selectedGitConnectionId = githubConnections.FirstOrDefault()?.Id;
        }
    }

    /// <summary>读取 GitHub 发布授权设置，用于决定连接按钮是否可用。</summary>
    private async Task LoadGitHubPublishAuthSettingsAsync()
    {
        _githubAuthorizationConfigured = await GitHubFlow.IsConfiguredAsync(default);
    }

    /// <summary>按路由恢复发布方案编辑状态；新建入口不预加载最近方案。</summary>
    private async Task LoadPublishPlanEditorAsync()
    {
        if (!IsAddPlanSection)
        {
            return;
        }

        if (PlanId is not { } planId)
        {
            return;
        }

        var plan = await PublishPlans.GetAsync(planId, default);
        if (plan is null)
        {
            _githubMessageIsDanger = true;
            _githubMessage = I18n["publish.addPlan.notFound"];
            return;
        }

        var config = GitHubPagesPublishConfiguration.FromJson(plan.ConfigurationJson).Normalize();
        _editingPlanId = plan.Id;
        _planDisplayName = plan.DisplayName;
        _publishChannel = plan.Channel;
        _selectedGitConnectionId = plan.GitProviderConnectionId;
        _githubOwner = config.Owner;
        _githubRepository = config.Repository;
        _githubBranch = config.Branch;
        _githubEnsurePagesSource = config.EnsurePagesSource;
        _githubDestinationMode = config.DestinationMode;
        _githubAllowBranchTakeover = config.AllowBranchTakeover;
        _githubNewRepository = string.IsNullOrWhiteSpace(config.Repository) ? _githubNewRepository : config.Repository;
        _githubRepositorySearch = GitHubRepositoryFullName();
    }
}
