using Bocchi.ContentModel;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>ContentEditor 顶栏主操作、溢出菜单与状态 pill 的展示逻辑。</summary>
public partial class ContentEditor
{
    /// <summary>顶栏状态 pill 仅在保存中或有脏数据时显示。</summary>
    private bool ShowCommandbarStatus => _busy || IsDirty;

    /// <summary>主保存按钮是否可点：未落盘草稿、有改动，或归档内容需恢复为草稿。</summary>
    private bool CanRunPrimarySave => !IsUnsavedDraft
        ? IsDirty || CurrentStatus == ContentStatus.Archived
        : true;

    /// <summary>是否存在可收进溢出菜单的次要操作。</summary>
    private bool HasCommandbarOverflow => HasEditorDocument && _error is null;

    /// <summary>主按钮文案：随草稿/发布/归档状态变化。</summary>
    private string CommandbarPrimaryLabel => _busy
        ? Text("contentEditor.action.saving")
        : IsUnsavedDraft
            ? Text("contentEditor.action.saveDraft")
            : Text("contentEditor.action.update");

    /// <summary>顶栏主按钮：暂存、更新或归档恢复。</summary>
    private Task RunPrimarySaveAsync()
    {
        if (IsUnsavedDraft)
        {
            return SaveDraftAsync();
        }

        if (CurrentStatus == ContentStatus.Archived)
        {
            return UpdateArchivedAsync();
        }

        return SaveAsync();
    }
}
