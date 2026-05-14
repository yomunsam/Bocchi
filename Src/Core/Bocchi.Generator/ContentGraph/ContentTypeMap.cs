namespace Bocchi.Generator.ContentGraph;

/// <summary>
/// 文件扩展名 → MIME 类型的最小映射。仅覆盖站点会用到的常见类型；未识别时回退到 <c>application/octet-stream</c>。
/// </summary>
internal static class ContentTypeMap
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".avif"] = "image/avif",
        [".ico"] = "image/x-icon",
        [".bmp"] = "image/bmp",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".flac"] = "audio/flac",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".xml"] = "application/xml; charset=utf-8",
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
    };

    public static string GuessFromFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var ext = Path.GetExtension(fileName);
        return ext.Length > 0 && ByExtension.TryGetValue(ext, out var mime)
            ? mime
            : "application/octet-stream";
    }
}
