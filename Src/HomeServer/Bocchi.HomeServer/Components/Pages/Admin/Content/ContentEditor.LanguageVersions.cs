using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>ContentEditor 的语言版本小组件与添加版本弹窗逻辑。</summary>
public partial class ContentEditor
{
    /// <summary>添加语言版本主按钮是否可提交。</summary>
    private bool CanCreateLanguageVersion =>
        !_busy &&
        !_languageVersionBusy &&
        _languageVersions is not null &&
        !string.IsNullOrWhiteSpace(_languageVersionTargetLanguage) &&
        (!_languageVersionIsTranslation || !string.IsNullOrWhiteSpace(_languageVersionSourceContentId));

    /// <summary>添加语言版本按钮的文案会随创建状态变化。</summary>
    private string CreateLanguageVersionPrimaryText => _languageVersionBusy
        ? Text("contentEditor.language.create.busy")
        : Text("contentEditor.language.create.primary");

    /// <summary>刷新当前文件的语言版本上下文；临时草稿不查 state store。</summary>
    private async Task LoadLanguageVersionsAsync()
    {
        _languageVersions = null;
        _languageVersionMessage = null;
        _showLanguageVersionDialog = false;
        _showLanguageVersionSaveReminder = false;
        _languageVersionTargetLanguage = string.Empty;
        _languageVersionSourceContentId = string.Empty;
        _copyCurrentContentToLanguageVersion = true;
        _languageVersionIsTranslation = false;

        if (_file is null || !SupportsLanguageVersions)
        {
            return;
        }

        try
        {
            _languageVersions = await LanguageVersions.GetAsync(_file.RelativePath);
            ResetLanguageVersionFormDefaults();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _languageVersionMessage = SaveFailedMessage(ex);
        }
    }

    /// <summary>打开添加语言版本弹窗；当前内容未保存时先提醒用户保存。</summary>
    private void OpenLanguageVersionDialog()
    {
        if (_busy || _languageVersionBusy)
        {
            return;
        }

        if (_file is null || IsDirty)
        {
            _showLanguageVersionSaveReminder = true;
            return;
        }

        ResetLanguageVersionFormDefaults();
        _showLanguageVersionDialog = true;
    }

    /// <summary>关闭添加语言版本弹窗并保留当前页面状态。</summary>
    private void CloseLanguageVersionDialog()
    {
        _showLanguageVersionDialog = false;
    }

    /// <summary>关闭保存提醒弹窗。</summary>
    private void CloseLanguageVersionSaveReminder()
    {
        _showLanguageVersionSaveReminder = false;
    }

    /// <summary>执行新语言版本创建；成功后跳转到新 variant 的编辑页。</summary>
    private async Task CreateLanguageVersionAsync()
    {
        if (_file is null || !CanCreateLanguageVersion)
        {
            return;
        }

        if (IsDirty)
        {
            _showLanguageVersionDialog = false;
            _showLanguageVersionSaveReminder = true;
            return;
        }

        _languageVersionBusy = true;
        _languageVersionMessage = null;
        try
        {
            var created = await LanguageVersions.CreateAsync(new CreateContentLanguageVariantRequest(
                _file.RelativePath,
                _languageVersionTargetLanguage,
                _copyCurrentContentToLanguageVersion,
                _languageVersionIsTranslation,
                _languageVersionIsTranslation ? _languageVersionSourceContentId : null));
            _showLanguageVersionDialog = false;
            Nav.NavigateTo(ContentEditingService.EditUrl(created.RelativePath), replace: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _languageVersionMessage = SaveFailedMessage(ex);
        }
        finally
        {
            _languageVersionBusy = false;
        }
    }

    /// <summary>根据当前版本上下文重置弹窗默认值。</summary>
    private void ResetLanguageVersionFormDefaults()
    {
        _languageVersionTargetLanguage = _languageVersions?.AvailableLanguages.Count > 0
            ? _languageVersions.AvailableLanguages[0].Code
            : string.Empty;
        _copyCurrentContentToLanguageVersion = true;
        _languageVersionIsTranslation = false;
        _languageVersionSourceContentId = _languageVersions?.Current.ContentId
            ?? (_languageVersions?.Variants.Count > 0 ? _languageVersions.Variants[0].ContentId : null)
            ?? string.Empty;
    }

    /// <summary>返回语言版本列表中用于展示的标题 fallback。</summary>
    private string LanguageVariantTitle(ContentLanguageVariantView variant)
        => string.IsNullOrWhiteSpace(variant.Title) ? Text("contentEditor.heading.untitled") : variant.Title;

    /// <summary>返回当前语言版本的 Native / Translation 标签。</summary>
    private string LanguageVariantKindLabel(ContentLanguageVariantView variant)
        => variant.IsTranslation
            ? Text("contentEditor.language.variant.translation")
            : Text("contentEditor.language.variant.native");

    /// <summary>返回目标语言选项展示文本。</summary>
    private static string LanguageOptionLabel(LanguageRecord language)
        => language.DisplayName;

    /// <summary>把「Native / English」形式的语言标签拆成主、次两段；无法拆分时 secondary 为 null。</summary>
    private static (string Primary, string? Secondary) SplitLanguageLabel(string label)
    {
        var separatorIndex = label.IndexOf(" / ", StringComparison.Ordinal);
        return separatorIndex < 0
            ? (label, null)
            : (label[..separatorIndex], label[(separatorIndex + 3)..]);
    }
}
