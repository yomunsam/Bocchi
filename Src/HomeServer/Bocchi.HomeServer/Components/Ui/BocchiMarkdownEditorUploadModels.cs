namespace Bocchi.HomeServer.Components.Ui;

/// <summary>浏览器把图片文件交给 Blazor 时使用的上传请求。</summary>
public sealed class MarkdownEditorImageUploadRequest
{
    /// <summary>浏览器提供的原始文件名；为空时服务端会使用图片 MIME 推导默认文件名。</summary>
    public string? FileName { get; set; }

    /// <summary>浏览器提供的 MIME 类型，例如 <c>image/png</c>。</summary>
    public string? ContentType { get; set; }

    /// <summary>图片文件内容流。paste、drop 和文件选择共用这一条上传通道。</summary>
    public Stream Content { get; set; } = Stream.Null;
}

/// <summary>图片上传后的编辑器插入结果。</summary>
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
