using Bocchi.HomeServer.Components.Ui;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>ContentEditor 窄屏下的写作 / 设置 / 预览 pane 切换。</summary>
public partial class ContentEditor
{
    /// <summary>窄屏编辑布局当前 pane。</summary>
    private EditorMobilePane _mobilePane = EditorMobilePane.Write;

    /// <summary>Markdown 编辑器组件引用，用于预览 pane 切换视图。</summary>
    private BocchiMarkdownEditor? _markdownEditor;

    /// <summary>传给 layout 的 pane 标识，驱动窄屏 CSS 显隐。</summary>
    private string MobilePaneCss => _mobilePane switch
    {
        EditorMobilePane.Settings => "settings",
        EditorMobilePane.Preview => "preview",
        _ => "write",
    };

    /// <summary>切换窄屏 pane；预览 pane 同步 Markdown 编辑器视图。</summary>
    private async Task SelectMobilePaneAsync(EditorMobilePane pane)
    {
        _mobilePane = pane;

        if (_markdownEditor is null)
        {
            return;
        }

        var viewMode = pane switch
        {
            EditorMobilePane.Preview => "preview",
            EditorMobilePane.Write => "write",
            _ => "write",
        };

        await _markdownEditor.SetEditorViewModeAsync(viewMode);
    }

    /// <summary>打开新文档时回到写作 pane。</summary>
    private void ResetMobilePaneState()
    {
        _mobilePane = EditorMobilePane.Write;
    }

    /// <summary>窄屏 pane 按钮样式。</summary>
    private string MobileTabClass(EditorMobilePane pane)
        => _mobilePane == pane
            ? "bocchi-editor-mobile-tabs__tab is-active"
            : "bocchi-editor-mobile-tabs__tab";

    /// <summary>button group 的 aria-pressed 字符串值。</summary>
    private static string AriaPressed(bool pressed) => pressed ? "true" : "false";

    /// <summary>窄屏编辑 pane。</summary>
    private enum EditorMobilePane
    {
        Write,
        Settings,
        Preview,
    }
}
