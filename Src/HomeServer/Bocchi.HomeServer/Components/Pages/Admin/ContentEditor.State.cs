using System.Globalization;

using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;
using Bocchi.HomeServer.Services.Ai;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

using Microsoft.AspNetCore.Components;

namespace Bocchi.HomeServer.Components.Pages.Admin;

/// <summary>ContentEditor 的页面状态和 Dashboard 文案派生属性。</summary>
public partial class ContentEditor
{
    /// <summary>查询字符串中的内容 workspace 相对路径。</summary>
    [SupplyParameterFromQuery(Name = "path")]
    public string? Path { get; set; }

    /// <summary>创建入口传入的内容类型，例如 post/page/work。</summary>
    [SupplyParameterFromQuery(Name = "kind")]
    public string? Kind { get; set; }

    /// <summary>尚未保存到 workspace 的编辑器临时草稿 id。</summary>
    [SupplyParameterFromQuery(Name = "draft")]
    public string? Draft { get; set; }

    /// <summary>未指定 path 时展示的内容候选列表。</summary>
    private IReadOnlyList<ContentSummary> _summaries = [];

    /// <summary>当前编辑文件。</summary>
    private EditableContentFile? _file;

    /// <summary>当前临时草稿；为空表示正在编辑已经落盘的内容文件。</summary>
    private EditorDraftSession? _draftSession;

    /// <summary>当前 Theme 声明的 Page template 下拉选项。</summary>
    private IReadOnlyList<ThemePageTemplateOption> _pageTemplates = [];

    /// <summary>Post 分类下拉选项；值保存为 frontmatter 中的 category name。</summary>
    private IReadOnlyList<CategoryOption> _postCategories = [];

    /// <summary>frontmatter YAML 编辑缓冲区；结构化字段保存时会回写到这里。</summary>
    private string _yaml = string.Empty;

    /// <summary>Markdown 正文编辑缓冲区。</summary>
    private string _markdown = string.Empty;

    /// <summary>载入时的原始 frontmatter，用于差异提示。</summary>
    private string _originalYaml = string.Empty;

    /// <summary>载入时的原始 Markdown，用于差异提示。</summary>
    private string _originalMarkdown = string.Empty;

    /// <summary>当前预览 HTML。</summary>
    private string _previewHtml = string.Empty;

    /// <summary>页面级错误提示。</summary>
    private string? _error;

    /// <summary>保存成功或失败后的短提示。</summary>
    private string? _saveMessage;

    /// <summary>保存中禁用按钮。</summary>
    private bool _busy;

    /// <summary>保存成功后给用户一个轻反馈。</summary>
    private bool _saved;

    /// <summary>标题字段。</summary>
    private string _title = string.Empty;

    /// <summary>slug 字段。</summary>
    private string _slug = string.Empty;

    /// <summary>当前页面生命周期内用户是否手动改过 slug；刷新或重新进入编辑页会恢复标题跟随。</summary>
    private bool _slugTouchedInSession;

    /// <summary>内容是否曾经发布过；一旦为 true，Path 永不再自动跟随标题变化。</summary>
    private bool _pathLocked;

    /// <summary>载入编辑器时内容是否已经锁定 Path；用于区分发布动作和已发布内容的普通保存。</summary>
    private bool _pathLockedAtLoad;

    /// <summary>发布状态字段，保存为 frontmatter 中的 status。</summary>
    private string _status = ContentStatus.Draft.ToString();

    /// <summary>摘要字段。</summary>
    private string _summary = string.Empty;

    /// <summary>Post 分类字段。</summary>
    private string _category = string.Empty;

    /// <summary>Post 标签字段，UI 使用逗号分隔，保存时写回 YAML sequence。</summary>
    private string _tagsText = string.Empty;

    /// <summary>Post 发布时间字段；保持文本输入是为了允许用户显式控制时区。</summary>
    private string _publishedAt = string.Empty;

    /// <summary>Page template 字段。</summary>
    private string _template = "normal";

    /// <summary>Page 是否进入导航栏。</summary>
    private bool _showInNavigation;

    /// <summary>Page 导航顺序。</summary>
    private int _order;

    /// <summary>浏览器侧 AI provider 探测结果。</summary>
    private AiAvailabilitySnapshot _aiAvailability = AiAvailabilitySnapshot.Empty;

    /// <summary>AI provider 是否完成过一次浏览器侧探测。</summary>
    private bool _aiAvailabilityChecked;

    /// <summary>AI slug 生成确认弹窗是否打开。</summary>
    private bool _showAiSlugConfirm;

    /// <summary>AI slug 生成失败弹窗是否打开。</summary>
    private bool _showAiSlugFailure;

    /// <summary>AI slug 生成流程是否正在运行。</summary>
    private bool _aiSlugBusy;

    /// <summary>当前已保存内容所在 localization group 的语言版本视图。</summary>
    private ContentLanguageVersionsView? _languageVersions;

    /// <summary>语言版本小组件的错误或提示信息。</summary>
    private string? _languageVersionMessage;

    /// <summary>添加语言版本弹窗是否打开。</summary>
    private bool _showLanguageVersionDialog;

    /// <summary>未保存时点击添加语言版本展示的保存提醒弹窗。</summary>
    private bool _showLanguageVersionSaveReminder;

    /// <summary>添加语言版本时选择的目标语言代码。</summary>
    private string _languageVersionTargetLanguage = string.Empty;

    /// <summary>是否把当前正文复制到新语言版本中。</summary>
    private bool _copyCurrentContentToLanguageVersion = true;

    /// <summary>新语言版本是否标记为 Translation variant。</summary>
    private bool _languageVersionIsTranslation;

    /// <summary>Translation variant 的来源内容 id。</summary>
    private string _languageVersionSourceContentId = string.Empty;

    /// <summary>语言版本创建流程是否正在运行。</summary>
    private bool _languageVersionBusy;

    /// <summary>当前编辑缓冲区是否相对载入版本发生变化。</summary>
    private bool IsDirty => CurrentYamlSnapshot != _originalYaml || _markdown != _originalMarkdown;

    /// <summary>当前页面是否已经载入可编辑文件或临时草稿。</summary>
    private bool HasEditorDocument => _file is not null || _draftSession is not null;

    /// <summary>当前是否正在编辑尚未落盘到 workspace 的临时草稿。</summary>
    private bool IsUnsavedDraft => _draftSession is not null && _file is null;

    /// <summary>当前内容是否为 Page。</summary>
    private bool IsPageFile => CurrentKind() == ContentKind.Page;

    /// <summary>当前内容是否为 Post。</summary>
    private bool IsPostFile => CurrentKind() == ContentKind.Post;

    /// <summary>当前内容是否为 Work。</summary>
    private bool IsWorkFile => CurrentKind() == ContentKind.Work;

    /// <summary>当前内容是否使用目录型 slug 管理 URL。</summary>
    private bool IsSlugManagedContent => IsPostFile || IsPageFile || IsWorkFile;

    /// <summary>当前文件是否是非默认语言 variant 文件。</summary>
    private bool IsLanguageVariantFile => IsVariantIndexMarkdownPath(_file?.RelativePath ?? Path);

    /// <summary>当前内容类型是否支持 M6 语言版本编辑体验。</summary>
    private bool SupportsLanguageVersions => CurrentKind() is ContentKind.Post or ContentKind.Page or ContentKind.Work;

    /// <summary>当前 slug 是否仍允许自动变化。</summary>
    private bool CanAutoChangeSlug => IsSlugManagedContent && !IsLanguageVariantFile && !_pathLocked && CurrentStatus != ContentStatus.Published;

    /// <summary>标题变化是否应继续带动 slug 变化。</summary>
    private bool CanFollowTitleSlug => CanAutoChangeSlug && !_slugTouchedInSession;

    /// <summary>AI slug 按钮是否应该出现在 slug 输入框内。</summary>
    private bool CanShowAiSlugButton => CanAutoChangeSlug && _aiAvailability.HasAvailableProvider;

    /// <summary>AI slug 当前是否可以触发。</summary>
    private bool CanUseAiSlug => CanShowAiSlugButton && !_busy && !_aiSlugBusy;

    /// <summary>当前 Page template 是否已经不在 active Theme contract 中。</summary>
    private bool TemplateUnavailable => IsPageFile &&
        !_pageTemplates.Any(template => string.Equals(template.Name, _template, StringComparison.Ordinal));

    /// <summary>当前 Post category 是否已经不在类别树中。</summary>
    private bool CategoryUnavailable => IsPostFile &&
        !string.IsNullOrWhiteSpace(_category) &&
        !_postCategories.Any(category => string.Equals(category.Name, _category, StringComparison.Ordinal));

    /// <summary>当前 frontmatter status 归一化后的枚举值。</summary>
    private ContentStatus CurrentStatus => NormalizeStatus(_status);

    /// <summary>浏览器标题栏显示的编辑页标题。</summary>
    private string EditorPageTitle => !HasEditorDocument
        ? Text("contentEditor.pageTitle.default")
        : _title.Length == 0
            ? Text("contentEditor.pageTitle.default")
            : _title;

    /// <summary>命令条上的上下文短标签。</summary>
    private string EditorKicker => !HasEditorDocument
        ? Text("contentEditor.kicker.empty")
        : string.Format(CultureInfo.CurrentCulture, "{0} · {1}", EditorKindLabel, EditorPathLabel);

    /// <summary>页面主标题：编辑中以文档实际标题作为 h1，避免出现样板词。</summary>
    private string EditorHeading
    {
        get
        {
            if (!HasEditorDocument)
            {
                return Text("contentEditor.heading.empty");
            }

            if (!string.IsNullOrWhiteSpace(_title))
            {
                return _title;
            }

            return Text("contentEditor.heading.untitled");
        }
    }

    /// <summary>无具体文档或编辑状态下的页头说明。</summary>
    private string EditorDescription => !HasEditorDocument
        ? Text("contentEditor.description.empty")
        : Text("contentEditor.description.editor");

    /// <summary>当前内容类型在 Dashboard UI 中的单数标签。</summary>
    private string EditorKindLabel => CurrentKind() switch
    {
        ContentKind.Page => Text("contentEditor.kind.page"),
        ContentKind.Work => Text("contentEditor.kind.work"),
        _ => Text("contentEditor.kind.post"),
    };

    /// <summary>返回当前内容类型列表的 Dashboard URL。</summary>
    private string BackUrl => CurrentKind() switch
    {
        ContentKind.Page => "/Admin/Pages",
        ContentKind.Work => "/Admin/Works",
        ContentKind.Post => "/Admin/Posts",
        _ => "/Admin/Content",
    };

    /// <summary>命令条状态胶囊的文案。</summary>
    private string EditorStateLabel => _saved
        ? Text("contentEditor.state.saved")
        : IsDirty
            ? Text("contentEditor.state.dirty")
            : Text("contentEditor.state.editing");

    /// <summary>侧栏和命令条中展示的内容位置。</summary>
    private string EditorPathLabel => IsUnsavedDraft
        ? Text("contentEditor.path.unsavedDraft")
        : _file?.RelativePath ?? Text("contentEditor.path.unselected");

    /// <summary>最后保存时间；临时草稿使用明确的未保存提示。</summary>
    private string LastSavedLabel => IsUnsavedDraft
        ? Text("contentEditor.lastSaved.unsaved")
        : _file?.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) ?? "-";

    /// <summary>保存和生成 slug 时使用的 fallback slug。</summary>
    private string CurrentFallbackSlug => _file is not null
        ? SlugFromPath(_file.RelativePath)
        : CurrentKind() switch
        {
            ContentKind.Page => "new-page",
            ContentKind.Work => "new-work",
            _ => "new-post",
        };

    /// <summary>标题输入框的本地化 placeholder。</summary>
    private string TitlePlaceholder => CurrentKind() switch
    {
        ContentKind.Page => Text("contentEditor.field.titlePlaceholder.page"),
        ContentKind.Work => Text("contentEditor.field.titlePlaceholder.work"),
        _ => Text("contentEditor.field.titlePlaceholder.post"),
    };

    /// <summary>当前 Markdown 正文的字符数与行数摘要。</summary>
    private string MarkdownStats
    {
        get
        {
            var chars = _markdown.Length;
            var lines = _markdown.Length == 0 ? 0 : _markdown.Replace("\r\n", "\n").Split('\n').Length;
            return FormatText("contentEditor.stats.format", chars, lines);
        }
    }

    /// <summary>保存前给用户扫读的轻量差异摘要。</summary>
    private string DiffSummary
    {
        get
        {
            var before = SplitLines(_originalYaml + "\n---body---\n" + _originalMarkdown);
            var after = SplitLines(CurrentYamlSnapshot + "\n---body---\n" + _markdown);
            var changed = 0;
            for (var i = 0; i < Math.Max(before.Length, after.Length); i++)
            {
                var oldLine = i < before.Length ? before[i] : string.Empty;
                var newLine = i < after.Length ? after[i] : string.Empty;
                if (!string.Equals(oldLine, newLine, StringComparison.Ordinal))
                {
                    changed++;
                }
            }

            return changed == 0
                ? Text("contentEditor.diff.noChanges")
                : FormatText("contentEditor.diff.changedFormat", changed);
        }
    }

    /// <summary>当前结构化字段回写后的 YAML 快照；YAML 无法解析时退回原始缓冲区。</summary>
    private string CurrentYamlSnapshot
        => TryBuildYamlFromFields(refreshUpdatedAt: false, out var yaml, out _) ? yaml : _yaml;

    /// <summary>把发布状态转成 Dashboard UI 当前语言下的标签。</summary>
    private string StatusLabel(ContentStatus status)
        => status switch
        {
            ContentStatus.Published => Text("contentEditor.status.published"),
            ContentStatus.Archived => Text("contentEditor.status.archived"),
            _ => Text("contentEditor.status.draft"),
        };

    /// <summary>把内容状态映射到已有状态胶囊色系。</summary>
    private static string StatusTone(ContentStatus status)
        => status switch
        {
            ContentStatus.Published => "success",
            ContentStatus.Archived => "neutral",
            _ => "warning",
        };

    /// <summary>读取当前 Dashboard UI 语言下的文案。</summary>
    private string Text(string key) => I18n[key];

    /// <summary>按当前文化格式化 Dashboard 文案。</summary>
    private string FormatText(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, I18n[key], args);

    /// <summary>把服务层 slug 校验代码转成 Dashboard UI 当前语言下的提示。</summary>
    private string LocalizeSlugValidation(ContentSlugValidationResult validation)
        => validation.Issue switch
        {
            ContentSlugValidationIssue.Empty => Text("contentEditor.slug.empty"),
            ContentSlugValidationIssue.ReservedRoute => Text("contentEditor.slug.reservedRoute"),
            ContentSlugValidationIssue.PageTaken => Text("contentEditor.slug.pageTaken"),
            ContentSlugValidationIssue.YearUnavailable => Text("contentEditor.slug.yearUnavailable"),
            ContentSlugValidationIssue.YearScopedTaken => Text("contentEditor.slug.yearScopedTaken"),
            _ => Text("contentEditor.slug.unavailable"),
        };

    /// <summary>把保存路径异常包装成当前 Dashboard UI 语言下的短提示。</summary>
    private string SaveFailedMessage(Exception exception)
        => FormatText("contentEditor.save.failedFormat", exception.Message);
}
