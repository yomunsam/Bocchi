using System.Globalization;
using Bocchi.Generator.Theme;
using Bocchi.GeneratorContract;
using Bocchi.HomeServer.Services;
using Bocchi.Workspace;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Components.Pages.Admin.Site;

/// <summary>Theme 管理页：查看当前前台主题、切换可用主题、安装 zip 包。</summary>
public partial class ThemeLibrary
{
    [Inject]
    private SiteProfileSettingsService SiteProfileSettings { get; set; } = default!;

    [Inject]
    private ThemeSettingsService ThemeSettings { get; set; } = default!;

    [Inject]
    private ThemeMigrationService ThemeMigration { get; set; } = default!;

    [Inject]
    private ThemePackageService ThemePackages { get; set; } = default!;

    [Inject]
    private DashboardLocalizationService I18n { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private BocchiDataLayout Layout { get; set; } = default!;

    [Inject]
    private IOptions<ThemePackageOptions> PackageOptions { get; set; } = default!;

    [Inject]
    private IOptions<ThemeDevelopmentOptions> ThemeDevelopment { get; set; } = default!;

    /// <summary>当前 active Theme id。</summary>
    private string _activeThemeId = "bocchi-mono";

    /// <summary>Theme Catalog 页面缓存。</summary>
    private IReadOnlyList<ThemeCatalogItem> _catalog = [];

    /// <summary>页面初次加载状态。</summary>
    private bool _loading = true;

    /// <summary>上传 inspection 运行中状态。</summary>
    private bool _uploadBusy;

    /// <summary>安装运行中状态。</summary>
    private bool _installBusy;

    /// <summary>切换 active Theme 运行中状态。</summary>
    private bool _activationBusy;

    /// <summary>安装区反馈文案。</summary>
    private string? _installFeedback;

    /// <summary>安装区反馈 tone。</summary>
    private string _installFeedbackTone = "neutral";

    /// <summary>页面级操作反馈（切换成功等）。</summary>
    private string? _pageFeedback;

    /// <summary>页面级反馈 tone。</summary>
    private string _pageFeedbackTone = "success";

    /// <summary>最近一次 zip inspection 结果。</summary>
    private ThemePackageInspection? _inspection;

    /// <summary>process runner 安装信任确认。</summary>
    private bool _trustProcessRunner;

    /// <summary>最近一次安装结果。</summary>
    private ThemePackageInstallResult? _installResult;

    /// <summary>Catalog 中 process Theme 激活前的显式信任确认。</summary>
    private HashSet<string> _trustedActivationThemeIds = new(StringComparer.Ordinal);

    /// <summary>等待 trust 对话框确认后激活的 Theme id。</summary>
    private string? _pendingTrustThemeId;

    /// <summary>等待 trust 的 Theme 展示名。</summary>
    private string? _pendingTrustThemeName;

    protected override async Task OnInitializedAsync()
    {
        await RefreshStateAsync();
        _loading = false;
    }

    /// <summary>当前 active Theme 的 Catalog 项。</summary>
    private ThemeCatalogItem? ActiveItem
        => _catalog.FirstOrDefault(item => IsActiveTheme(item.Id));

    /// <summary>排序后的 Catalog：当前 → 可用 → 名称。</summary>
    private IEnumerable<ThemeCatalogItem> OrderedCatalog
        => _catalog
            .OrderByDescending(item => IsActiveTheme(item.Id))
            .ThenByDescending(item => item.IsAvailable)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>当前主题是否健康可用。</summary>
    private bool ActiveIsHealthy => ActiveItem?.IsAvailable == true;

    /// <summary>是否启用 Theme Dev Link（Development 默认开启，Production 需显式配置）。</summary>
    private bool IsThemeDevelopmentMode => ThemeDevelopment.Value.AreDevLinksEnabled;

    /// <summary><c>dev-links.json</c> 在 DataRoot 下的绝对路径。</summary>
    private string DevLinksPath => Path.Combine(Layout.ThemesDirectory, "dev-links.json");

    /// <summary>Dev Link 清单示例，供开发者区域展示。</summary>
    private const string DevLinksExampleJson =
        """
        {
          "schemaVersion": "1.0",
          "links": [
            {
              "id": "my-theme",
              "root": "/absolute/path/to/theme",
              "enabled": true
            }
          ]
        }
        """;

    /// <summary>安装按钮是否可用。</summary>
    private bool CanInstallInspection
        => !_installBusy &&
           _inspection?.IsInstallable == true &&
           (!_inspection.RequiresTrust || _trustProcessRunner);

    /// <summary>安装按钮文案。</summary>
    private string InstallActionLabel
        => _installBusy ? I18n["themeLibrary.install.installing"] : I18n["themeLibrary.install.action"];

    /// <summary>读取 active Theme 与 Catalog。</summary>
    private async Task RefreshStateAsync()
    {
        var site = await SiteProfileSettings.GetAsync();
        _activeThemeId = string.IsNullOrWhiteSpace(site.DefaultThemeId) ? "bocchi-mono" : site.DefaultThemeId;
        _catalog = await ThemeSettings.ListThemeCatalogAsync();
    }

    /// <summary>用户请求切换主题；process runner 先走 trust 对话框。</summary>
    private void RequestActivation(ThemeCatalogItem item)
    {
        if (_activationBusy || IsActiveTheme(item.Id) || !item.IsAvailable)
        {
            return;
        }

        if (RequiresProcessTrust(item) && !IsActivationTrusted(item.Id))
        {
            _pendingTrustThemeId = item.Id;
            _pendingTrustThemeName = item.Name;
            return;
        }

        _ = SetActiveThemeAsync(item.Id);
    }

    /// <summary>trust 对话框确认后激活。</summary>
    private async Task ConfirmTrustAndActivateAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingTrustThemeId))
        {
            return;
        }

        _trustedActivationThemeIds.Add(_pendingTrustThemeId);
        var themeId = _pendingTrustThemeId;
        _pendingTrustThemeId = null;
        _pendingTrustThemeName = null;
        await SetActiveThemeAsync(themeId);
    }

    /// <summary>关闭 trust 对话框。</summary>
    private void CancelTrustDialog()
    {
        _pendingTrustThemeId = null;
        _pendingTrustThemeName = null;
    }

    /// <summary>用户最近一次选择的 zip 文件名，用于上传区展示。</summary>
    private string? _selectedFileName;

    /// <summary>上传区样式：选中 / 检查中。</summary>
    private string UploadDropzoneClass
    {
        get
        {
            var classes = "bocchi-themes-install__dropzone";
            if (!string.IsNullOrWhiteSpace(_selectedFileName))
            {
                classes += " has-file";
            }

            if (_uploadBusy)
            {
                classes += " is-busy";
            }

            return classes;
        }
    }

    /// <summary>保存上传文件并执行 inspection。</summary>
    private async Task InspectUploadAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        _selectedFileName = file.Name;
        _uploadBusy = true;
        _installFeedback = null;
        _inspection = null;
        _installResult = null;
        _trustProcessRunner = false;

        try
        {
            var uploadDirectory = Path.Combine(Layout.ThemeUploadCacheDirectory, "browser-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(uploadDirectory);
            var uploadPath = Path.Combine(uploadDirectory, Path.GetFileName(file.Name));
            await using (var source = file.OpenReadStream(PackageOptions.Value.MaxPackageBytes))
            await using (var target = File.Create(uploadPath))
            {
                await source.CopyToAsync(target);
            }

            _inspection = await ThemePackages.InspectZipAsync(uploadPath);
            _installFeedback = _inspection.IsInstallable
                ? I18n["themeLibrary.install.review.pass"]
                : I18n["themeLibrary.install.review.fail"];
            _installFeedbackTone = _inspection.IsInstallable ? "success" : "danger";
        }
        catch (IOException)
        {
            _installFeedback = I18n["themeLibrary.install.readFailed"];
            _installFeedbackTone = "danger";
        }
        finally
        {
            _uploadBusy = false;
        }
    }

    /// <summary>安装 inspection 通过的 Theme Package。</summary>
    private async Task InstallInspectionAsync()
    {
        if (_inspection is null || !CanInstallInspection)
        {
            return;
        }

        _installBusy = true;
        _installFeedback = null;
        try
        {
            _installResult = await ThemePackages.InstallOrUpdateAsync(_inspection, _trustProcessRunner);
            if (_inspection.RequiresTrust)
            {
                _trustedActivationThemeIds.Add(_installResult.ThemeId);
            }

            await RefreshStateAsync();
            _installFeedback = _installResult.WasUpdate
                ? I18n["themeLibrary.install.doneUpdate"]
                : I18n["themeLibrary.install.done"];
            _installFeedbackTone = "success";
            _inspection = null;
            _trustProcessRunner = false;
        }
        catch (InvalidOperationException)
        {
            _installFeedback = I18n["themeLibrary.install.failed"];
            _installFeedbackTone = "danger";
        }
        finally
        {
            _installBusy = false;
        }
    }

    /// <summary>安装完成后立即切换。</summary>
    private async Task ActivateInstalledAsync()
    {
        if (_installResult is null)
        {
            return;
        }

        await SetActiveThemeAsync(_installResult.ThemeId);
        _installResult = null;
    }

    /// <summary>把指定 Theme 设为 active；需要迁移时跳转向导。</summary>
    private async Task SetActiveThemeAsync(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId) || IsActiveTheme(themeId))
        {
            return;
        }

        var item = _catalog.FirstOrDefault(candidate => string.Equals(candidate.Id, themeId, StringComparison.Ordinal));
        if (item is null || !item.IsAvailable)
        {
            return;
        }

        if (RequiresProcessTrust(item) && !IsActivationTrusted(item.Id))
        {
            return;
        }

        _activationBusy = true;
        _pageFeedback = null;
        var current = await SiteProfileSettings.GetAsync();
        var plan = await ThemeMigration.ScanAsync(current.DefaultThemeId, themeId);
        if (plan.Entries.Count > 0)
        {
            Navigation.NavigateTo($"/Admin/Site/ThemeMigration?from={Uri.EscapeDataString(current.DefaultThemeId)}&to={Uri.EscapeDataString(themeId)}");
            return;
        }

        await SiteProfileSettings.SaveAsync(new SiteProfileSettingsUpdate
        {
            SiteName = current.SiteName,
            DefaultTitle = current.DefaultTitle,
            Description = current.Description,
            PublicBaseUrl = current.PublicBaseUrl,
            CopyrightNotice = current.CopyrightNotice,
            Language = current.Language,
            TimeZone = current.TimeZone,
            DefaultThemeId = themeId,
        });
        await RefreshStateAsync();
        _pageFeedback = I18n["themeLibrary.feedback.switched"];
        _pageFeedbackTone = "success";
        _activationBusy = false;
    }

    /// <summary>process runner Theme 激活前需要显式 trust。</summary>
    private static bool RequiresProcessTrust(ThemeCatalogItem item)
        => string.Equals(item.RunnerKind, "process", StringComparison.OrdinalIgnoreCase);

    /// <summary>是否已确认激活 trust。</summary>
    private bool IsActivationTrusted(string themeId)
        => _trustedActivationThemeIds.Contains(themeId);

    /// <summary>是否为当前 active Theme。</summary>
    private bool IsActiveTheme(string themeId)
        => string.Equals(themeId, _activeThemeId, StringComparison.Ordinal);

    /// <summary>主题卡片 meta 行：版本 · 来源 · runner。</summary>
    private string ThemeMetaLine(ThemeCatalogItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Version))
        {
            parts.Add(string.Format(CultureInfo.CurrentCulture, I18n["themeLibrary.versionFormat"], item.Version));
        }

        parts.Add(SourceLabel(item.SourceKind));
        if (!string.IsNullOrWhiteSpace(item.RunnerKind))
        {
            parts.Add(RunnerLabel(item.RunnerKind));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>来源用户文案。</summary>
    private string SourceLabel(ThemeSourceKind sourceKind)
        => sourceKind switch
        {
            ThemeSourceKind.BuiltIn => I18n["themeLibrary.list.source.builtIn"],
            ThemeSourceKind.Installed => I18n["themeLibrary.list.source.installed"],
            ThemeSourceKind.DevLink => I18n["themeLibrary.list.source.devLink"],
            ThemeSourceKind.PackageCandidate => I18n["themeLibrary.list.source.packageCandidate"],
            _ => I18n["themeLibrary.unknown"],
        };

    /// <summary>Runner 用户文案。</summary>
    private string RunnerLabel(string runnerKind)
        => string.Equals(runnerKind, "process", StringComparison.OrdinalIgnoreCase)
            ? I18n["themeLibrary.list.runner.process"]
            : I18n["themeLibrary.list.runner.static"];

    /// <summary>诊断 tone。</summary>
    private static string DiagnosticTone(ThemeDiagnostic diagnostic)
        => diagnostic.Severity switch
        {
            ThemeDiagnosticSeverity.Error => "danger",
            ThemeDiagnosticSeverity.Warning => "warning",
            _ => "neutral",
        };

    /// <summary>诊断文案。</summary>
    private string DiagnosticText(ThemeDiagnostic diagnostic)
    {
        var key = $"themeDiagnostics.{diagnostic.Code}";
        var text = I18n[key];
        return string.Equals(text, key, StringComparison.Ordinal)
            ? diagnostic.Code
            : text;
    }
}
