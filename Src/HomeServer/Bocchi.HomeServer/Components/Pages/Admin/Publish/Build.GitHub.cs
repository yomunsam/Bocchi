using System.Globalization;

using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Git;
using Bocchi.HomeServer.Services.Publishing;

using Microsoft.AspNetCore.Components;

namespace Bocchi.HomeServer.Components.Pages.Admin.Publish;

/// <summary>发布中心页面的 GitHub 相关逻辑：连接、仓库、Device Flow、方案保存和校验。</summary>
public partial class Build
{
    /// <summary>切换 GitHub 账号后清空账号相关的仓库候选，避免把旧账号的 repo 误保存到新账号。</summary>
    private async Task OnGitHubConnectionChanged(ChangeEventArgs args)
    {
        _selectedGitConnectionId = int.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
        ClearSelectedGitHubRepository();
        _githubRepositories = [];
        _githubRepositoryPickerOpen = false;
        if (string.Equals(_githubDestinationMode, "existing", StringComparison.Ordinal))
        {
            await LoadGitHubRepositoriesAsync(force: true);
        }
    }

    /// <summary>切换发布仓库模式；进入已有仓库时立即准备候选列表，形成搜索下拉的预期反馈。</summary>
    private async Task OnGitHubDestinationModeChanged(ChangeEventArgs args)
    {
        var mode = args.Value?.ToString();
        _githubDestinationMode = string.Equals(mode, "existing", StringComparison.Ordinal) ? "existing" : "create";
        _githubBranchCheck = null;
        _githubAllowBranchTakeover = false;
        if (string.Equals(_githubDestinationMode, "existing", StringComparison.Ordinal))
        {
            _githubRepositorySearch = GitHubRepositoryFullName();
            _githubRepositoryPickerOpen = true;
            await LoadGitHubRepositoriesAsync();
        }
        else
        {
            _githubRepositoryPickerOpen = false;
        }
    }

    /// <summary>打开已有 repository 搜索下拉；首次打开时拉取当前账号可访问的仓库。</summary>
    private async Task OpenGitHubRepositoryPickerAsync()
    {
        _githubRepositoryPickerOpen = true;
        await LoadGitHubRepositoriesAsync();
    }

    /// <summary>手动刷新 GitHub repository 列表，适配用户刚在 GitHub 创建或授权新仓库的情况。</summary>
    private async Task RefreshGitHubRepositoriesAsync()
    {
        _githubRepositoryPickerOpen = true;
        await LoadGitHubRepositoriesAsync(force: true);
    }

    /// <summary>更新 repository 搜索文本；文本变化时清空旧选择，避免保存隐藏的旧 owner/repo。</summary>
    private void OnGitHubRepositorySearchInput(ChangeEventArgs args)
    {
        _githubRepositorySearch = args.Value?.ToString() ?? string.Empty;
        _githubRepositoryPickerOpen = true;
        _githubBranchCheck = null;
        _githubAllowBranchTakeover = false;
        if (!string.Equals(_githubRepositorySearch.Trim(), GitHubRepositoryFullName(), StringComparison.OrdinalIgnoreCase))
        {
            ClearSelectedGitHubRepository();
        }
    }

    /// <summary>选择已有 GitHub repository，并把 owner/name 回填到发布配置模型。</summary>
    private void SelectGitHubRepository(GitHubRepository repository)
    {
        _githubOwner = repository.Owner;
        _githubRepository = repository.Name;
        _githubRepositorySearch = repository.FullName;
        _githubRepositoryPickerOpen = false;
        _githubBranchCheck = null;
        _githubAllowBranchTakeover = false;
    }

    /// <summary>从 GitHub 读取当前账号可访问的 repository；失败时只显示表单错误，不让页面崩溃。</summary>
    private async Task LoadGitHubRepositoriesAsync(bool force = false)
    {
        if (!HasSelectedGitHubConnection || _githubRepositoriesLoading)
        {
            return;
        }

        if (!force && _githubRepositories.Count > 0)
        {
            return;
        }

        _githubRepositoriesLoading = true;
        try
        {
            var token = await GetSelectedGitHubAccessTokenAsync();
            _githubRepositories = await GitHubFlow.ListRepositoriesAsync(token, default);
            _githubMessageIsDanger = false;
            _githubMessage = null;
        }
        catch (Exception)
        {
            _githubMessageIsDanger = true;
            _githubMessage = I18n["publish.github.errorRepositoryListFailed"];
        }
        finally
        {
            _githubRepositoriesLoading = false;
        }
    }

    /// <summary>返回搜索下拉中应该展示的 repository；限制条数保证表单高度稳定。</summary>
    private IReadOnlyList<GitHubRepository> FilteredGitHubRepositories()
    {
        var query = _githubRepositorySearch.Trim();
        var matches = string.IsNullOrWhiteSpace(query)
            ? _githubRepositories
            : _githubRepositories
                .Where(x => x.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        return matches.Take(12).ToArray();
    }

    /// <summary>尝试把手动输入的 owner/repository 写回目标模型，作为 GitHub 列表不完整时的兜底。</summary>
    private bool TryApplyGitHubRepositorySearch()
    {
        if (!string.Equals(_githubDestinationMode, "existing", StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(_githubOwner)
            || !string.IsNullOrWhiteSpace(_githubRepository))
        {
            return true;
        }

        var fullName = _githubRepositorySearch.Trim();
        var slashIndex = fullName.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex <= 0 || slashIndex >= fullName.Length - 1)
        {
            return false;
        }

        var owner = fullName[..slashIndex].Trim();
        var repository = fullName[(slashIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository) || repository.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        _githubOwner = owner;
        _githubRepository = repository;
        _githubRepositorySearch = GitHubRepositoryFullName();
        return true;
    }

    /// <summary>清空已有 repository 选择，但保留 branch，因为 branch 通常仍是 gh-pages。</summary>
    private void ClearSelectedGitHubRepository()
    {
        _githubOwner = string.Empty;
        _githubRepository = string.Empty;
    }

    /// <summary>当前 GitHub repository 的 owner/name 全名；未选中时返回空字符串。</summary>
    private string GitHubRepositoryFullName()
        => string.IsNullOrWhiteSpace(_githubOwner) || string.IsNullOrWhiteSpace(_githubRepository)
            ? string.Empty
            : $"{_githubOwner}/{_githubRepository}";

    /// <summary>发起 GitHub Device Flow。</summary>
    private async Task StartGitHubDeviceFlowAsync()
    {
        _githubMessage = null;
        _githubMessageIsDanger = false;
        if (!await GitHubFlow.IsConfiguredAsync(default))
        {
            _githubMessageIsDanger = true;
            _githubMessage = I18n["publish.github.errorOAuthNotConfigured"];
            _githubAuthorizationConfigured = false;
            return;
        }

        var start = await GitHubFlow.StartAsync(default);
        _githubDeviceCode = start.DeviceCode;
        _githubUserCode = start.UserCode;
        _githubVerificationUri = start.VerificationUri;
        _githubMessage = I18n["publish.github.authorizationStarted"];
    }

    /// <summary>完成 GitHub Device Flow，并把授权账号保存为 provider connection。</summary>
    private async Task CompleteGitHubDeviceFlowAsync()
    {
        if (string.IsNullOrWhiteSpace(_githubDeviceCode))
        {
            return;
        }

        var poll = await GitHubFlow.PollAsync(_githubDeviceCode, default);
        if (poll.Status != GitHubDeviceFlowPollStatus.Succeeded || poll.Credential is null)
        {
            _githubMessageIsDanger = poll.Status is GitHubDeviceFlowPollStatus.AccessDenied or GitHubDeviceFlowPollStatus.Expired or GitHubDeviceFlowPollStatus.Failed;
            _githubMessage = poll.Status switch
            {
                GitHubDeviceFlowPollStatus.Pending => I18n["publish.github.authorizationPending"],
                GitHubDeviceFlowPollStatus.SlowDown => I18n["publish.github.authorizationSlowDown"],
                GitHubDeviceFlowPollStatus.AccessDenied => I18n["publish.github.authorizationDenied"],
                GitHubDeviceFlowPollStatus.Expired => I18n["publish.github.authorizationExpired"],
                _ => poll.ErrorDescription ?? I18n["publish.github.authorizationFailed"],
            };
            return;
        }

        var user = await GitHubFlow.GetCurrentUserAsync(poll.Credential.AccessToken, default);
        var credential = poll.Credential with { GitHubLogin = user.Login };
        var saved = await GitConnections.SaveAsync(
            new GitProviderConnectionSaveInput(
                null,
                GitProviderKeys.GitHub,
                "https://github.com",
                user.Login,
                credential.Scope,
                credential.ToJson()),
            default);

        _selectedGitConnectionId = saved.Id;
        _githubDeviceCode = null;
        _githubUserCode = null;
        _githubVerificationUri = null;
        _githubMessageIsDanger = false;
        _githubMessage = I18n["publish.github.authorizationSucceeded"];
        await LoadGitConnectionsAsync();
        if (string.Equals(_githubDestinationMode, "existing", StringComparison.Ordinal))
        {
            await LoadGitHubRepositoriesAsync(force: true);
        }
    }

    /// <summary>创建发布专用 GitHub repository，并回填 owner/repository。</summary>
    private async Task CreateGitHubRepositoryAsync()
    {
        if (!await TryRequireGitHubConnectionAsync())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_githubNewRepository))
        {
            _githubMessageIsDanger = true;
            _githubMessage = I18n["publish.github.errorRepositoryRequired"];
            return;
        }

        var token = await GetSelectedGitHubAccessTokenAsync();
        var repo = await GitHubFlow.CreateRepositoryAsync(token, _githubNewRepository, _githubCreatePrivateRepository, autoInit: true, default);
        _githubOwner = repo.Owner;
        _githubRepository = repo.Name;
        _githubRepositorySearch = repo.FullName;
        _githubDestinationMode = "create";
        _githubBranchCheck = new GitHubPublishBranchCheck(GitHubPublishBranchState.Missing, false, 0);
        _githubMessageIsDanger = false;
        _githubMessage = string.Format(CultureInfo.CurrentCulture, I18n["publish.github.repositoryCreatedFormat"], repo.FullName);
    }

    /// <summary>检查已有发布 branch 是否带 Bocchi marker。</summary>
    private async Task CheckGitHubBranchAsync()
    {
        if (!ValidateGitHubTarget(requireRepository: true) || !await TryRequireGitHubConnectionAsync())
        {
            return;
        }

        var token = await GetSelectedGitHubAccessTokenAsync();
        _githubBranchCheck = await GitHubFlow.CheckPublishBranchAsync(token, _githubOwner, _githubRepository, _githubBranch, default);
        _githubMessageIsDanger = _githubBranchCheck.State == GitHubPublishBranchState.Occupied;
        _githubMessage = _githubBranchCheck.State switch
        {
            GitHubPublishBranchState.Missing => I18n["publish.github.branchMissing"],
            GitHubPublishBranchState.Empty => I18n["publish.github.branchEmpty"],
            GitHubPublishBranchState.ManagedByBocchi => I18n["publish.github.branchManaged"],
            _ => I18n["publish.github.branchOccupied"],
        };
    }

    /// <summary>保存 GitHub Pages 发布方案；凭据来自 Git provider connection。</summary>
    private async Task SaveGitHubPlanAsync()
    {
        if (!ValidateGitHubPlan())
        {
            return;
        }

        if (!await EnsureExistingGitHubBranchIsSafeAsync())
        {
            return;
        }

        _githubSaving = true;
        try
        {
            var config = new GitHubPagesPublishConfiguration
            {
                Owner = _githubOwner,
                Repository = _githubRepository,
                Branch = _githubBranch,
                EnsurePagesSource = _githubEnsurePagesSource,
                DestinationMode = _githubDestinationMode,
                AllowBranchTakeover = _githubAllowBranchTakeover,
            }.Normalize();
            var saved = await PublishPlans.SaveAsync(
                new PublishPlanSaveInput(
                    _editingPlanId,
                    _planDisplayName,
                    _publishChannel,
                    config.ToJson(),
                    CredentialJson: null,
                    SetAsDefault: true,
                    GitProviderConnectionId: _selectedGitConnectionId),
                default);

            _editingPlanId = saved.Id;
            _githubOwner = config.Owner;
            _githubRepository = config.Repository;
            _githubBranch = config.Branch;
            _githubRepositorySearch = GitHubRepositoryFullName();
            _githubMessageIsDanger = false;
            _githubMessage = I18n["publish.addPlan.saved"];
            await RefreshAsync();
        }
        finally
        {
            _githubSaving = false;
        }
    }

    /// <summary>保存已有 repository 方案前强制检查发布 branch，避免用户绕过检查按钮。</summary>
    private async Task<bool> EnsureExistingGitHubBranchIsSafeAsync()
    {
        if (!string.Equals(_githubDestinationMode, "existing", StringComparison.Ordinal))
        {
            return true;
        }

        if (!await TryRequireGitHubConnectionAsync())
        {
            return false;
        }

        var token = await GetSelectedGitHubAccessTokenAsync();
        _githubBranchCheck = await GitHubFlow.CheckPublishBranchAsync(token, _githubOwner, _githubRepository, _githubBranch, default);
        if (_githubBranchCheck.State == GitHubPublishBranchState.Occupied && !_githubAllowBranchTakeover)
        {
            _githubMessageIsDanger = true;
            _githubMessage = I18n["publish.github.errorBranchTakeoverRequired"];
            return false;
        }

        return true;
    }

    /// <summary>校验发布方案表单；错误文案全部来自 i18n。</summary>
    private bool ValidateGitHubPlan()
    {
        _githubMessageIsDanger = true;
        if (string.IsNullOrWhiteSpace(_planDisplayName))
        {
            _githubMessage = I18n["publish.addPlan.errorNameRequired"];
            return false;
        }

        if (!string.Equals(_publishChannel, PublishPlanService.GitHubPagesChannel, StringComparison.Ordinal))
        {
            _githubMessage = I18n["publish.addPlan.errorUnsupportedChannel"];
            return false;
        }

        if (_selectedGitConnectionId is null)
        {
            _githubMessage = I18n["publish.github.errorConnectionRequired"];
            return false;
        }

        if (!ValidateGitHubTarget(requireRepository: true))
        {
            return false;
        }

        if (_githubBranchCheck?.State == GitHubPublishBranchState.Occupied && !_githubAllowBranchTakeover)
        {
            _githubMessage = I18n["publish.github.errorBranchTakeoverRequired"];
            return false;
        }

        _githubMessageIsDanger = false;
        return true;
    }

    /// <summary>校验 GitHub 目标坐标。</summary>
    private bool ValidateGitHubTarget(bool requireRepository)
    {
        _githubMessageIsDanger = true;
        if (requireRepository && !TryApplyGitHubRepositorySearch())
        {
            _githubMessage = I18n["publish.github.errorRepositoryRequired"];
            return false;
        }

        if (requireRepository && string.IsNullOrWhiteSpace(_githubOwner))
        {
            _githubMessage = I18n["publish.github.errorOwnerRequired"];
            return false;
        }

        if (requireRepository && string.IsNullOrWhiteSpace(_githubRepository))
        {
            _githubMessage = I18n["publish.github.errorRepositoryRequired"];
            return false;
        }

        if (string.IsNullOrWhiteSpace(_githubBranch))
        {
            _githubMessage = I18n["publish.github.errorBranchRequired"];
            return false;
        }

        _githubMessageIsDanger = false;
        return true;
    }

    /// <summary>确保已经选择 GitHub provider connection。</summary>
    private Task<bool> TryRequireGitHubConnectionAsync()
    {
        if (_selectedGitConnectionId is not null)
        {
            return Task.FromResult(true);
        }

        _githubMessageIsDanger = true;
        _githubMessage = I18n["publish.github.errorConnectionRequired"];
        return Task.FromResult(false);
    }

    /// <summary>读取当前 GitHub connection 的 access token。</summary>
    private async Task<string> GetSelectedGitHubAccessTokenAsync()
    {
        var connection = _selectedGitConnectionId is null
            ? null
            : await GitConnections.GetAsync(_selectedGitConnectionId.Value, default);
        if (connection is null)
        {
            throw new InvalidOperationException("GitHub connection is missing.");
        }

        var credential = GitHubOAuthCredential.FromJson(GitConnections.UnprotectCredentialJson(connection));
        return credential.AccessToken;
    }
}
