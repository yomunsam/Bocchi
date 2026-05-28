using Bocchi.Generator.Pipeline;
using Bocchi.Generator.State;
using Bocchi.HomeServer.Build;
using Bocchi.HomeServer.Data;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Git;
using Bocchi.HomeServer.Services.Publishing;

using Blazicons;

using Microsoft.AspNetCore.Components;

namespace Bocchi.HomeServer.Components.Pages.Admin.Publish;

/// <summary>发布中心页面的状态定义：常量、路由参数、私有字段和派生属性。</summary>
public partial class Build
{
    private const string LocalOutputSection = "LocalOutput";
    private const string AddPlanSection = "AddPlan";

    /// <summary>发布中心子路由片段；为空时展示默认"一键发布"首页。</summary>
    [Parameter]
    public string? Section { get; set; }

    /// <summary>发布方案编辑 id；为空表示新建方案。</summary>
    [Parameter]
    public int? PlanId { get; set; }

    /// <summary>一键发布运行中时禁用主操作，避免重复触发构建或远端发布。</summary>
    private bool _busy;

    /// <summary>当前页面触发的最近一次本地生成结果。</summary>
    private BuildResult? _lastResult;

    /// <summary>历史构建投影，来自 SQLite 状态库。</summary>
    private IReadOnlyList<BuildRunSummary>? _recent;

    /// <summary>最近发布运行投影，来自 Home Server EF 状态库。</summary>
    private IReadOnlyList<PublishRunRecord>? _publishRuns;

    /// <summary>发布方案列表；首页用它决定一键发布目标，发布方案区用它渲染空态或列表。</summary>
    private IReadOnlyList<PublishPlanRecord>? _publishPlans;

    /// <summary>Git provider 连接列表；发布方案向导可复用已有授权。</summary>
    private IReadOnlyList<GitProviderConnectionRecord>? _gitConnections;

    /// <summary>当前 GitHub 账号可选的 repository 列表；只在进入已有仓库选择时加载。</summary>
    private IReadOnlyList<GitHubRepository> _githubRepositories = [];

    /// <summary>已有 repository 选择器的搜索文本，同时允许输入 owner/repository 作为兜底。</summary>
    private string _githubRepositorySearch = string.Empty;

    /// <summary>repository 列表正在从 GitHub 加载。</summary>
    private bool _githubRepositoriesLoading;

    /// <summary>是否展开已有 repository 搜索下拉层。</summary>
    private bool _githubRepositoryPickerOpen;

    /// <summary>一键发布后的短状态消息。</summary>
    private string? _oneClickMessage;

    /// <summary>一键发布消息是否为失败态。</summary>
    private bool _oneClickMessageIsDanger;

    /// <summary>当前编辑的发布方案 id；为空表示新建。</summary>
    private int? _editingPlanId;

    /// <summary>发布方案名称，由用户定义，不使用渠道名代替。</summary>
    private string _planDisplayName = string.Empty;

    /// <summary>当前发布渠道；v1 只启用 GitHub Pages。</summary>
    private string _publishChannel = PublishPlanService.GitHubPagesChannel;

    /// <summary>当前选择的 Git provider 连接 id。</summary>
    private int? _selectedGitConnectionId;

    /// <summary>GitHub repository owner。</summary>
    private string _githubOwner = string.Empty;

    /// <summary>GitHub repository name。</summary>
    private string _githubRepository = string.Empty;

    /// <summary>创建发布专用 repository 时使用的名称。</summary>
    private string _githubNewRepository = "bocchi-site";

    /// <summary>是否创建 private repository；GitHub Pages 可用性取决于账号计划。</summary>
    private bool _githubCreatePrivateRepository;

    /// <summary>发布目标 repo 模式：create 或 existing。</summary>
    private string _githubDestinationMode = "create";

    /// <summary>GitHub Pages 输出 branch。</summary>
    private string _githubBranch = "gh-pages";

    /// <summary>是否发布后自动确保 GitHub Pages source 指向 branch 根目录。</summary>
    private bool _githubEnsurePagesSource = true;

    /// <summary>用户是否明确接管已有无 marker 的发布 branch。</summary>
    private bool _githubAllowBranchTakeover;

    /// <summary>GitHub Device Flow device code；只在授权流程中短暂保存。</summary>
    private string? _githubDeviceCode;

    /// <summary>GitHub Device Flow 用户验证码。</summary>
    private string? _githubUserCode;

    /// <summary>GitHub Device Flow 验证页面。</summary>
    private string? _githubVerificationUri;

    /// <summary>最近一次 branch 安全检查。</summary>
    private GitHubPublishBranchCheck? _githubBranchCheck;

    /// <summary>保存 GitHub Pages 配置时禁用按钮。</summary>
    private bool _githubSaving;

    /// <summary>GitHub Pages 配置面板的短状态消息。</summary>
    private string? _githubMessage;

    /// <summary>GitHub Pages 配置面板消息是否为失败态。</summary>
    private bool _githubMessageIsDanger;

    /// <summary>当前是否已经配置 GitHub OAuth App client id。</summary>
    private bool _githubAuthorizationConfigured;

    /// <summary>未启用 GitHub 授权时，把禁用按钮和解释文案关联起来。</summary>
    private string? GitHubAuthUnavailableNoticeId => _githubAuthorizationConfigured ? null : "github-auth-unavailable";

    /// <summary>规范化后的发布中心子页面。</summary>
    private string CurrentSection
    {
        get
        {
            if (string.Equals(Section, LocalOutputSection, StringComparison.OrdinalIgnoreCase))
            {
                return LocalOutputSection;
            }

            if (string.Equals(Section, AddPlanSection, StringComparison.OrdinalIgnoreCase))
            {
                return AddPlanSection;
            }

            return string.Empty;
        }
    }

    /// <summary>当前是否展示默认发布首页。</summary>
    private bool IsPublishHomeSection => string.IsNullOrEmpty(CurrentSection);

    /// <summary>当前是否展示本地输出子页面。</summary>
    private bool IsLocalOutputSection => CurrentSection == LocalOutputSection;

    /// <summary>当前是否展示添加发布方案子页面。</summary>
    private bool IsAddPlanSection => CurrentSection == AddPlanSection;

    /// <summary>默认发布方案；没有显式默认时使用列表第一项作为旧数据回退。</summary>
    private PublishPlanRecord? DefaultPublishPlan
    {
        get
        {
            if (_publishPlans is not { Count: > 0 } plans)
            {
                return null;
            }

            return plans.FirstOrDefault(x => x.IsDefault) ?? plans[0];
        }
    }

    /// <summary>可用于发布向导的 GitHub 连接集合。</summary>
    private IEnumerable<GitProviderConnectionRecord> GitHubConnections
        => _gitConnections?.Where(x => x.ProviderKey == GitProviderKeys.GitHub)
            ?? Array.Empty<GitProviderConnectionRecord>();

    /// <summary>当前选中的 GitHub 连接；不存在时说明发布向导仍停留在授权步骤。</summary>
    private GitProviderConnectionRecord? SelectedGitHubConnection
        => _selectedGitConnectionId is null
            ? null
            : GitHubConnections.FirstOrDefault(x => x.Id == _selectedGitConnectionId.Value);

    /// <summary>是否已经有可用 GitHub 连接，决定仓库配置步骤是否展示。</summary>
    private bool HasSelectedGitHubConnection => SelectedGitHubConnection is not null;

    /// <summary>当前页面标题。</summary>
    private string CurrentPageTitle => CurrentSection switch
    {
        LocalOutputSection => I18n["publish.localOutput.pageTitle"],
        AddPlanSection => I18n["publish.addPlan.pageTitle"],
        _ => I18n["publish.page.title"],
    };

    /// <summary>当前页面 eyebrow。</summary>
    private string CurrentEyebrow => CurrentSection switch
    {
        LocalOutputSection => I18n["publish.localOutput.eyebrow"],
        AddPlanSection => I18n["publish.addPlan.eyebrow"],
        _ => I18n["publish.page.eyebrow"],
    };

    /// <summary>当前页面主标题。</summary>
    private string CurrentHeading => CurrentSection switch
    {
        LocalOutputSection => I18n["publish.localOutput.heading"],
        AddPlanSection => I18n["publish.addPlan.heading"],
        _ => I18n["publish.page.heading"],
    };

    /// <summary>当前页面说明。</summary>
    private string CurrentDescription => CurrentSection switch
    {
        LocalOutputSection => I18n["publish.localOutput.description"],
        AddPlanSection => I18n["publish.addPlan.description"],
        _ => I18n["publish.page.description"],
    };
}
