using Bocchi.ContentModel;
using Bocchi.Workspace.Content;

namespace Bocchi.Workspace.Scanning;

/// <summary>一次扫描的最终结果。</summary>
/// <param name="ScanRunId">数据库中的扫描运行 ID。</param>
/// <param name="StartedAt">开始时间。</param>
/// <param name="FinishedAt">结束时间。</param>
/// <param name="FilesScanned">扫描的源文件数。</param>
/// <param name="ItemsLoaded">成功加载的内容项数。</param>
/// <param name="Errors">本次扫描产生的所有 Error。</param>
/// <param name="Warnings">本次扫描产生的所有 Warning。</param>
/// <param name="Infos">本次扫描产生的所有 Info。</param>
/// <param name="Posts">已加载的文章。</param>
/// <param name="Pages">已加载的页面。</param>
/// <param name="Works">已加载的作品。</param>
/// <param name="Notes">已加载的短文。</param>
/// <param name="FriendLinks">已加载的友链。</param>
/// <param name="SiteSettings">已加载的站点设置（可空）。</param>
public sealed record ScanResult(
    long ScanRunId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int FilesScanned,
    int ItemsLoaded,
    IReadOnlyList<ContentValidationError> Errors,
    IReadOnlyList<ContentValidationError> Warnings,
    IReadOnlyList<ContentValidationError> Infos,
    IReadOnlyList<PostDocument> Posts,
    IReadOnlyList<PageDocument> Pages,
    IReadOnlyList<WorkDocument> Works,
    IReadOnlyList<NoteDocument> Notes,
    IReadOnlyList<FriendLink> FriendLinks,
    SiteSettings? SiteSettings)
{
    public TimeSpan Duration => FinishedAt - StartedAt;

    public bool HasErrors => Errors.Count > 0;
}