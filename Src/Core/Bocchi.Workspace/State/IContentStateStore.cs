using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;

namespace Bocchi.Workspace.State;

/// <summary>SQLite 中保存的一份内容摘要（用于在 UI 列举/预览，不含正文）。</summary>
/// <param name="Kind">内容类型。</param>
/// <param name="ContentId">业务 id（slug 或 Note.Id 等）。</param>
/// <param name="Title">标题（短文取摘要，可为 <c>null</c>）。</param>
/// <param name="Status">状态。</param>
/// <param name="Year">年份（仅 Post / Work / Note / Photo），可空。</param>
/// <param name="PublishedAt">发布时间，可空。</param>
/// <param name="UpdatedAt">更新时间，可空。</param>
/// <param name="RelativePath">源文件相对内容空间根的路径。</param>
public sealed record ContentSummary(
    ContentKind Kind,
    string ContentId,
    string? Title,
    ContentStatus Status,
    string? Year,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt,
    string RelativePath);

/// <summary>一次扫描运行的元数据。</summary>
/// <param name="Id">数据库主键。</param>
/// <param name="StartedAt">开始时间（UTC）。</param>
/// <param name="FinishedAt">结束时间（UTC），未完成时为 <c>null</c>。</param>
/// <param name="FilesScanned">扫描的文件数。</param>
/// <param name="ItemsLoaded">成功加载的内容项数。</param>
/// <param name="ErrorCount">错误条数。</param>
/// <param name="WarningCount">警告条数。</param>
/// <param name="GitHeadSha">扫描时内容空间 Git HEAD SHA，可空。</param>
/// <param name="Status">运行状态：<c>running</c> / <c>succeeded</c> / <c>failed</c>。</param>
public sealed record ScanRunRecord(
    long Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int FilesScanned,
    int ItemsLoaded,
    int ErrorCount,
    int WarningCount,
    string? GitHeadSha,
    string Status);

/// <summary>一份要写入 <see cref="IContentStateStore"/> 的内容摘要。</summary>
public sealed record ContentItemUpsert(
    ContentKind Kind,
    string ContentId,
    string? Slug,
    string? Title,
    ContentStatus Status,
    string? Year,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt,
    string? FrontmatterJson,
    string SourceRelativePath);

/// <summary>一份要写入 <see cref="IContentStateStore"/> 的源文件记录。</summary>
public sealed record FileUpsert(
    string RelativePath,
    ContentKind Kind,
    string Sha256,
    DateTimeOffset LastModifiedUtc);

/// <summary>SQLite 状态库的对外契约。</summary>
public interface IContentStateStore
{
    /// <summary>开启一次新扫描运行，返回其 ID。</summary>
    Task<long> StartScanRunAsync(DateTimeOffset startedAt, string? gitHeadSha, CancellationToken cancellationToken = default);

    /// <summary>结束一次扫描运行（写入计数与状态）。</summary>
    Task FinishScanRunAsync(
        long scanRunId,
        DateTimeOffset finishedAt,
        int filesScanned,
        int itemsLoaded,
        int errorCount,
        int warningCount,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>upsert 一条文件记录。</summary>
    Task<long> UpsertFileAsync(FileUpsert file, CancellationToken cancellationToken = default);

    /// <summary>upsert 一条内容摘要。可以关联 fileId（可空）。</summary>
    Task UpsertContentItemAsync(ContentItemUpsert item, long? fileId, CancellationToken cancellationToken = default);

    /// <summary>把一次扫描运行产生的错误一次性写入。</summary>
    Task AppendErrorsAsync(long scanRunId, IEnumerable<ContentValidationError> errors, CancellationToken cancellationToken = default);

    /// <summary>读取最近一次（任意状态）扫描运行。</summary>
    Task<ScanRunRecord?> GetLatestScanRunAsync(CancellationToken cancellationToken = default);

    /// <summary>读取指定扫描运行的错误列表。</summary>
    Task<IReadOnlyList<ContentValidationError>> ListErrorsAsync(long scanRunId, CancellationToken cancellationToken = default);

    /// <summary>列举指定 kind 的内容摘要。<paramref name="kind"/> 为 <c>null</c> 表示全部。</summary>
    Task<IReadOnlyList<ContentSummary>> ListContentSummariesAsync(ContentKind? kind, CancellationToken cancellationToken = default);
}
