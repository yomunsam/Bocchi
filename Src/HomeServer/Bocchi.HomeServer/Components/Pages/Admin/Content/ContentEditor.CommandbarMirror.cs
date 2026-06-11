using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bocchi.HomeServer.Components.Pages.Admin.Content;

/// <summary>ContentEditor 顶栏滚动 mirror：标题滚出视口时在 commandbar 显示文档标题。</summary>
public partial class ContentEditor : IAsyncDisposable
{
    /// <summary>标题区 anchor 是否仍在 sticky 顶栏下方可见。</summary>
    private bool _titleFieldVisible = true;

    /// <summary>标题 mirror 是否已绑定到当前文档。</summary>
    private bool _titleMirrorMounted;

    /// <summary>当前 mirror 绑定的文档 key，用于 path/draft 切换时重建 observer。</summary>
    private string? _titleMirrorDocumentKey;

    /// <summary>滚动 mirror 模块引用。</summary>
    private IJSObjectReference? _titleMirrorModule;

    /// <summary>JS 回调引用。</summary>
    private DotNetObjectReference<ContentEditor>? _titleMirrorDotNetRef;

    /// <summary>编辑页根元素，包含 commandbar 与 writing surface。</summary>
    private ElementReference _editorPageRoot;

    /// <summary>放在标题输入框下方的零高度 sentinel，供 IntersectionObserver 判断滚出。</summary>
    private ElementReference _titleScrollAnchor;

    /// <summary>顶栏上下文文案：标题可见时显示路径，滚出后 mirror 文档标题。</summary>
    private string CommandbarContextLabel => _titleFieldVisible ? EditorKicker : EditorHeading;

    /// <summary>顶栏上下文样式：mirror 标题时使用正文字色以区别于路径 kicker。</summary>
    private string CommandbarContextClass => _titleFieldVisible
        ? "bocchi-editor-commandbar__context"
        : "bocchi-editor-commandbar__context bocchi-editor-commandbar__context--title";

    /// <summary>path/draft 切换或离开编辑态时重置 mirror 状态。</summary>
    private void ResetTitleMirrorState()
    {
        _titleFieldVisible = true;
        _titleMirrorMounted = false;
        _titleMirrorDocumentKey = null;
    }

    /// <summary>当前编辑文档的 mirror 绑定 key。</summary>
    private string? TitleMirrorDocumentKey
    {
        get
        {
            if (!HasEditorDocument || _error is not null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(Path))
            {
                return $"path:{Path}";
            }

            if (!string.IsNullOrWhiteSpace(Draft))
            {
                return $"draft:{Draft}";
            }

            return null;
        }
    }

    /// <summary>IntersectionObserver 通知标题区可见性变化。</summary>
    [JSInvokable]
    public Task SetTitleFieldVisible(bool visible)
    {
        if (_titleFieldVisible == visible)
        {
            return Task.CompletedTask;
        }

        _titleFieldVisible = visible;
        return InvokeAsync(StateHasChanged);
    }

    /// <summary>文档载入后挂载标题 mirror；同一文档不重复绑定。</summary>
    private async Task EnsureTitleMirrorMountedAsync()
    {
        var documentKey = TitleMirrorDocumentKey;
        if (documentKey is null)
        {
            await DisposeTitleMirrorAsync();
            return;
        }

        if (_titleMirrorMounted && string.Equals(_titleMirrorDocumentKey, documentKey, StringComparison.Ordinal))
        {
            return;
        }

        await DisposeTitleMirrorAsync();
        _titleMirrorDocumentKey = documentKey;

        _titleMirrorModule ??= await Js.InvokeAsync<IJSObjectReference>(
            "import",
            "./Components/Pages/Admin/Content/ContentEditor.razor.js");
        _titleMirrorDotNetRef ??= DotNetObjectReference.Create(this);

        await _titleMirrorModule.InvokeVoidAsync(
            "mountTitleMirror",
            _editorPageRoot,
            _titleMirrorDotNetRef,
            _titleScrollAnchor);

        _titleMirrorMounted = true;
    }

    /// <summary>释放 IntersectionObserver 与 resize 监听。</summary>
    private async Task DisposeTitleMirrorAsync()
    {
        if (_titleMirrorModule is not null)
        {
            try
            {
                await _titleMirrorModule.InvokeVoidAsync("releaseTitleMirror");
            }
            catch (JSDisconnectedException)
            {
                // circuit 断开时无需再访问浏览器实例。
            }
        }

        _titleMirrorMounted = false;
        _titleMirrorDocumentKey = null;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeTitleMirrorAsync();

        if (_titleMirrorModule is not null)
        {
            try
            {
                await _titleMirrorModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _titleMirrorDotNetRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
