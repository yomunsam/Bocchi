namespace Bocchi.ContentModel;

/// <summary>
/// 媒体引用，统一以站点输出路径（站点根相对路径）表示，避免暴露本机绝对路径。
/// </summary>
/// <param name="Path">站点根下的相对路径，例如 <c>/media/images/cover.jpg</c>。</param>
/// <param name="Alt">可选的替代文本。</param>
public sealed record MediaReference(string Path, string? Alt = null);
