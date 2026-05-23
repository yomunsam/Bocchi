using Bocchi.ContentModel;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Components.Pages;

/// <summary>ContentEditor 的状态动作与删除确认逻辑；避免把页面标记文件继续撑大。</summary>
public partial class ContentEditor
{
    /// <summary>当前删除确认弹窗的目标语义。</summary>
    private DeleteConfirmMode _deleteConfirmMode = DeleteConfirmMode.Draft;

    /// <summary>删除确认弹窗是否打开。</summary>
    private bool _showDeleteConfirm;

    /// <summary>删除确认标题，区分草稿删除与归档内容的彻底删除。</summary>
    private string DeleteConfirmTitle => _deleteConfirmMode == DeleteConfirmMode.Permanent
        ? "彻底删除内容"
        : "删除草稿";

    /// <summary>删除确认按钮文案；归档内容使用更明确的不可逆表述。</summary>
    private string DeleteConfirmPrimaryText => _deleteConfirmMode == DeleteConfirmMode.Permanent
        ? "彻底删除"
        : "删除";

    /// <summary>删除确认正文，提醒用户这会删除 workspace 源文件和关联资产。</summary>
    private string DeleteConfirmBody => _deleteConfirmMode == DeleteConfirmMode.Permanent
        ? "这会删除当前内容的源文件、同目录资产和内容索引，删除后无法从 Bocchi 内恢复。"
        : "这会删除这个已保存草稿的源文件、同目录资产和内容索引。";

    /// <summary>未保存临时草稿的暂存动作：第一次创建正式 Markdown 文件。</summary>
    private Task SaveDraftAsync()
        => SaveNewDraftToWorkspaceAsync(ContentStatus.Draft);

    /// <summary>把已发布内容撤回为草稿，但保留源文件和正文。</summary>
    private async Task WithdrawAsync()
    {
        _status = ContentStatus.Draft.ToString();
        await SaveAsync();
    }

    /// <summary>把已发布内容归档；归档只改变 frontmatter 状态，不删除源文件。</summary>
    private async Task ArchiveAsync()
    {
        _status = ContentStatus.Archived.ToString();
        await SaveAsync();
    }

    /// <summary>归档内容再次更新时转回草稿，避免保存后仍保持不可见的归档状态。</summary>
    private async Task UpdateArchivedAsync()
    {
        _status = ContentStatus.Draft.ToString();
        await SaveAsync();
    }

    /// <summary>打开已保存草稿的删除确认弹窗。</summary>
    private void OpenDraftDeleteConfirm()
    {
        _deleteConfirmMode = DeleteConfirmMode.Draft;
        _showDeleteConfirm = true;
    }

    /// <summary>打开归档内容的彻底删除确认弹窗。</summary>
    private void OpenPermanentDeleteConfirm()
    {
        _deleteConfirmMode = DeleteConfirmMode.Permanent;
        _showDeleteConfirm = true;
    }

    /// <summary>关闭删除确认弹窗。</summary>
    private void CloseDeleteConfirm()
    {
        _showDeleteConfirm = false;
    }

    /// <summary>执行已确认的删除，并清理源文件对应的扫描索引。</summary>
    private async Task ConfirmDeleteAsync()
    {
        var deletePath = _file?.RelativePath ?? Path;
        if (string.IsNullOrWhiteSpace(deletePath) || _busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await Editor.DeleteAsync(deletePath);
            await Store.DeleteContentBySourcePathAsync(deletePath);
            _showDeleteConfirm = false;
            Nav.NavigateTo(BackUrl, replace: true);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>把未保存临时草稿落入 workspace，并在成功后切换到真实文件 URL。</summary>
    private async Task SaveNewDraftToWorkspaceAsync(ContentStatus targetStatus)
    {
        if (_draftSession is null || _busy)
        {
            return;
        }

        _status = targetStatus.ToString();
        if (IsSlugManagedContent && targetStatus == ContentStatus.Published)
        {
            _pathLocked = true;
        }

        if (IsPostFile && targetStatus == ContentStatus.Published && string.IsNullOrWhiteSpace(_publishedAt))
        {
            _publishedAt = FormatDateTime(Time.GetUtcNow());
        }

        if (!TryPrepareSlugForSave(out var slugError))
        {
            _saveMessage = slugError;
            return;
        }

        if (!TryBuildYamlFromFields(out var yamlToSave, out var yamlError))
        {
            _saveMessage = yamlError;
            return;
        }

        _busy = true;
        try
        {
            var saved = await Editor.CreateFromDraftAsync(
                _draftSession.Kind,
                yamlToSave,
                _markdown,
                _draftSession.AssetsDirectory);
            await Scanner.ScanAsync();
            await Drafts.DeleteAsync(_draftSession.DraftId);

            _file = saved;
            _draftSession = null;
            Path = saved.RelativePath;
            Draft = null;
            _yaml = saved.Yaml;
            _originalYaml = saved.Yaml;
            _originalMarkdown = saved.Markdown;
            _previewHtml = saved.PreviewHtml;
            _saved = true;
            _saveMessage = targetStatus == ContentStatus.Published
                ? "已发布并刷新内容索引。"
                : "已暂存并刷新内容索引。";
            Nav.NavigateTo(ContentEditingService.EditUrl(saved.RelativePath), replace: true);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>删除确认的两种文案模式。</summary>
    private enum DeleteConfirmMode
    {
        /// <summary>删除已保存草稿。</summary>
        Draft,

        /// <summary>彻底删除已归档内容。</summary>
        Permanent,
    }
}
