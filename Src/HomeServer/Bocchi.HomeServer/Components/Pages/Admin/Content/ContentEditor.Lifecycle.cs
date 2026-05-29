using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>ContentEditor 的载入、保存和编辑缓冲区同步逻辑。</summary>
public partial class ContentEditor
{
    protected override async Task OnParametersSetAsync()
    {
        _saved = false;
        _saveMessage = null;
        _error = null;
        if (!string.IsNullOrWhiteSpace(Path))
        {
            if (!IsMarkdownPath(Path))
            {
                _error = Text("contentEditor.invalidMarkdownPath");
                return;
            }

            try
            {
                _draftSession = null;
                _file = await Editor.ReadAsync(Path);
                await LoadEditorOptionsAsync();
                LoadFileIntoEditor(_file);
                await LoadLanguageVersionsAsync();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _error = ex.Message;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(Draft))
        {
            try
            {
                _file = null;
                _draftSession = await Drafts.ReadAsync(Draft);
                await LoadEditorOptionsAsync();
                LoadDraftIntoEditor(_draftSession);
                await LoadLanguageVersionsAsync();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _error = ex.Message;
            }

            return;
        }

        if (TryParseCreateKind(Kind, out var createKind))
        {
            try
            {
                _file = null;
                _draftSession = await Drafts.CreateAsync(createKind);
                Draft = _draftSession.DraftId;
                Nav.NavigateTo(EditorDraftService.EditUrl(_draftSession.DraftId), replace: true);
            }
            catch (InvalidOperationException ex)
            {
                _error = ex.Message;
            }

            return;
        }

        _summaries = (await Store.ListContentSummariesAsync(null))
            .Where(x => IsMarkdownPath(x.RelativePath))
            .ToList();
        _file = null;
        _draftSession = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _aiAvailability = await Ai.GetAvailabilityAsync();
        _aiAvailabilityChecked = true;
        StateHasChanged();
    }

    /// <summary>保存文件、刷新扫描投影，并重算预览。</summary>
    private async Task SaveAsync()
    {
        var savePath = _file?.RelativePath ?? Path;
        if (string.IsNullOrWhiteSpace(savePath) || _busy)
        {
            return;
        }

        if (!TryPrepareSlugForSave(out var slugError))
        {
            _saveMessage = slugError;
            return;
        }

        if (IsSlugManagedContent && CurrentStatus == ContentStatus.Published)
        {
            _pathLocked = true;
        }

        if (!TryBuildYamlFromFields(out var yamlToSave, out var yamlError))
        {
            _saveMessage = yamlError;
            return;
        }

        _busy = true;
        try
        {
            var originalPath = savePath.Replace('\\', '/');
            var saved = await Editor.SaveAsync(savePath, yamlToSave, _markdown, allowPathRename: !_pathLockedAtLoad);
            await Scanner.ScanAsync();
            if (!string.Equals(originalPath, saved.RelativePath, StringComparison.Ordinal))
            {
                await Store.DeleteContentBySourcePathAsync(originalPath);
            }

            _file = saved;
            Path = saved.RelativePath;
            _yaml = saved.Yaml;
            _originalYaml = saved.Yaml;
            _originalMarkdown = saved.Markdown;
            _previewHtml = saved.PreviewHtml;
            _pathLockedAtLoad = _pathLocked;
            await LoadLanguageVersionsAsync();
            _saved = true;
            _saveMessage = Text("contentEditor.save.saved");
            if (!string.Equals(originalPath, saved.RelativePath, StringComparison.Ordinal))
            {
                Nav.NavigateTo(ContentEditingService.EditUrl(saved.RelativePath), replace: true);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _saveMessage = SaveFailedMessage(ex);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>把当前草稿切到 Published 后保存；Post 缺发布时间时补当前时间。</summary>
    private async Task PublishAsync()
    {
        if (IsUnsavedDraft)
        {
            await SaveNewDraftToWorkspaceAsync(ContentStatus.Published);
            return;
        }

        _status = ContentStatus.Published.ToString();
        if (IsSlugManagedContent)
        {
            _pathLocked = true;
        }

        if (IsPostFile && string.IsNullOrWhiteSpace(_publishedAt))
        {
            _publishedAt = FormatDateTime(Time.GetUtcNow());
        }

        await SaveAsync();
    }

    private Task OnMarkdownChangedAsync(string value)
    {
        _markdown = value;
        _saved = false;
        RefreshPreview();
        return Task.CompletedTask;
    }

    /// <summary>用编辑缓冲区重算预览。</summary>
    private void RefreshPreview()
    {
        _previewHtml = RewriteEditorAssetReferences(Markdown.RenderHtml(_markdown));
    }

    private void LoadFileIntoEditor(EditableContentFile file)
    {
        _yaml = file.Yaml;
        _markdown = file.Markdown;
        RefreshPreview();
        _saved = false;
        LoadMetadataFromYaml(file);
        ResetOriginalSnapshots();
    }

    /// <summary>把尚未落盘的编辑器临时草稿载入同一套编辑缓冲区。</summary>
    private void LoadDraftIntoEditor(EditorDraftSession draft)
    {
        _yaml = draft.Yaml;
        _markdown = draft.Markdown;
        RefreshPreview();
        _saved = false;
        LoadMetadataFromYaml(draft.Yaml, CurrentFallbackSlug);
        ResetOriginalSnapshots();
    }

    /// <summary>载入完成后用编辑器同一套 YAML 快照作为干净基线，避免序列化规范化造成打开即脏。</summary>
    private void ResetOriginalSnapshots()
    {
        _originalYaml = CurrentYamlSnapshot;
        _originalMarkdown = _markdown;
    }

    /// <summary>按当前内容类型载入写作页所需的下拉选项。</summary>
    private async Task LoadEditorOptionsAsync()
    {
        _postCategories = IsPostFile
            ? FlattenCategoryOptions((await Categories.GetAsync(ContentKind.Post)).Roots)
            : [];

        if (!IsPageFile)
        {
            _pageTemplates = [];
            return;
        }

        var settings = await SiteProfile.GetAsync();
        var themeId = string.IsNullOrWhiteSpace(settings.DefaultThemeId)
            ? (await ThemeSettings.GetDefaultAsync()).ThemeId
            : settings.DefaultThemeId;
        var contract = await ThemeSettings.GetPageContractAsync(themeId, I18n.CurrentLanguage.Code);
        _pageTemplates = contract.PageTemplates;
    }
}
