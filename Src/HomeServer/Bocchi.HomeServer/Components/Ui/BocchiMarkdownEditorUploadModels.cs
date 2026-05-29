namespace Bocchi.HomeServer.Components.Ui;

/// <summary>浏览器粘贴图片后传给 Blazor 的上传请求。</summary>
public sealed class MarkdownEditorPastedImageRequest
{
    /// <summary>浏览器提供的原始文件名；为空时服务端会使用图片 MIME 推导默认文件名。</summary>
    public string? FileName { get; set; }

    /// <summary>浏览器提供的 MIME 类型，例如 <c>image/png</c>。</summary>
    public string? ContentType { get; set; }

    /// <summary>图片文件内容流。当前只用于 paste image，后续拖拽/文件选择可复用同一请求模型。</summary>
    public Stream Content { get; set; } = Stream.Null;
}

/// <summary>粘贴图片上传后的编辑器插入结果。</summary>
public sealed class MarkdownEditorAssetUploadResult
{
    /// <summary>上传成功后需要插入 CodeMirror 光标处的 Markdown。</summary>
    public string? Markdown { get; set; }

    /// <summary>上传失败时展示给用户的 Dashboard 本地化错误。</summary>
    public string? Error { get; set; }

    /// <summary>构造成功结果。</summary>
    public static MarkdownEditorAssetUploadResult Success(string markdown)
        => new() { Markdown = markdown };

    /// <summary>构造失败结果。</summary>
    public static MarkdownEditorAssetUploadResult Failure(string error)
        => new() { Error = error };
}
