using Bocchi.ContentModel;
using Bocchi.Workspace.Scanning;
using Bocchi.Workspace.State;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Data.State;

/// <summary>
/// <see cref="IContentStateStore"/> 的 EF Core 实现。ContentScanner 是 singleton，
/// 所以这里通过 <see cref="IDbContextFactory{TContext}"/> 为每次调用创建短生命周期的 DbContext。
/// </summary>
public sealed class ContentStateStore : IContentStateStore
{
    private readonly IDbContextFactory<BocchiDbContext> _dbFactory;
    private readonly TimeProvider _time;

    /// <summary>创建内容状态库。</summary>
    public ContentStateStore(IDbContextFactory<BocchiDbContext> dbFactory, TimeProvider time)
    {
        _dbFactory = dbFactory;
        _time = time;
    }

    public async Task<long> StartScanRunAsync(DateTimeOffset startedAt, string? gitHeadSha, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var run = new ContentScanRunRecord { StartedAt = startedAt, GitHeadSha = gitHeadSha, Status = "running" };
        db.ContentScanRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return run.Id;
    }

    public async Task FinishScanRunAsync(
        long scanRunId, DateTimeOffset finishedAt, int filesScanned, int itemsLoaded,
        int errorCount, int warningCount, string status, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await db.ContentScanRuns
            .Where(x => x.Id == scanRunId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.FinishedAt, finishedAt)
                .SetProperty(x => x.FilesScanned, filesScanned)
                .SetProperty(x => x.ItemsLoaded, itemsLoaded)
                .SetProperty(x => x.ErrorCount, errorCount)
                .SetProperty(x => x.WarningCount, warningCount)
                .SetProperty(x => x.Status, status), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<long> UpsertFileAsync(FileUpsert file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var record = await db.ContentFiles
            .SingleOrDefaultAsync(x => x.RelativePath == file.RelativePath, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new ContentFileRecord { RelativePath = file.RelativePath };
            db.ContentFiles.Add(record);
        }

        record.Kind = file.Kind;
        record.Sha256 = file.Sha256;
        record.LastModifiedAt = file.LastModifiedUtc;
        record.LastSeenAt = _time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record.Id;
    }

    public async Task UpsertContentItemAsync(ContentItemUpsert item, long fileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // 目录型内容和站点配置一个源文件只对应一条记录：slug 改名后旧记录要随之消失；友链文件会产生多条记录。
        if (item.Kind is not ContentKind.FriendLink)
        {
            await db.ContentItems
                .Where(x => x.FileId == fileId && x.Kind == item.Kind && x.ContentId != item.ContentId)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var record = await db.ContentItems
            .SingleOrDefaultAsync(x => x.Kind == item.Kind && x.ContentId == item.ContentId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            record = new ContentItemRecord { Kind = item.Kind, ContentId = item.ContentId };
            db.ContentItems.Add(record);
        }

        record.FileId = fileId;
        record.Slug = item.Slug;
        record.Title = item.Title;
        record.Status = item.Status;
        record.Year = item.Year;
        record.PublishedAt = item.PublishedAt;
        record.UpdatedAt = item.UpdatedAt;
        record.FrontmatterJson = item.FrontmatterJson;
        record.Language = item.Language;
        record.LocalizationGroup = item.LocalizationGroup;
        record.IsTranslation = item.IsTranslation;
        record.SourceLanguage = item.SourceLanguage;
        record.SourceContentId = item.SourceContentId;
        record.LastSeenAt = _time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AppendErrorsAsync(long scanRunId, IEnumerable<ContentValidationError> errors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(errors);
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.ContentErrors.AddRange(errors.Select(e => new ContentErrorRecord
        {
            ScanRunId = scanRunId,
            RelativePath = e.RelativePath,
            Kind = e.Kind,
            Field = e.Field,
            Severity = e.Severity,
            Code = e.Code,
            Message = e.Message,
        }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScanRunRecord?> GetLatestScanRunAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ContentScanRuns
            .AsNoTracking()
            .OrderByDescending(x => x.Id)
            .Select(x => new ScanRunRecord(
                x.Id, x.StartedAt, x.FinishedAt, x.FilesScanned, x.ItemsLoaded,
                x.ErrorCount, x.WarningCount, x.GitHeadSha, x.Status))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ContentValidationError>> ListErrorsAsync(long scanRunId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.ContentErrors
            .AsNoTracking()
            .Where(x => x.ScanRunId == scanRunId)
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Id)
            .Select(x => new ContentValidationError(x.RelativePath, x.Kind, x.Field, x.Severity, x.Code, x.Message))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ContentSummary>> ListContentSummariesAsync(ContentKind? kind, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.ContentItems.AsNoTracking();
        if (kind is not null)
        {
            query = query.Where(x => x.Kind == kind.Value);
        }

        // 发布时间倒序，未发布（无时间）的排在最后。
        var items = await query
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.PublishedAt == null)
            .ThenByDescending(x => x.PublishedAt)
            .Select(x => new ContentSummary(
                x.Kind, x.ContentId, x.Title, x.Status, x.Year, x.PublishedAt, x.UpdatedAt,
                x.File!.RelativePath, x.Language, x.LocalizationGroup, x.IsTranslation,
                x.SourceLanguage, x.SourceContentId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 库里存的是 UTC，列表按站点时区展示。
        var timeZoneId = await db.SiteProfileSettings.AsNoTracking()
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (timeZoneId is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            return items;
        }

        return items
            .Select(x => x with
            {
                PublishedAt = x.PublishedAt is { } published ? TimeZoneInfo.ConvertTime(published, timeZone) : null,
                UpdatedAt = x.UpdatedAt is { } updated ? TimeZoneInfo.ConvertTime(updated, timeZone) : null,
            })
            .ToList();
    }

    public async Task DeleteContentBySourcePathAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = relativePath.Replace('\\', '/');
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // 内容条目随文件记录级联删除（见 BocchiDbContext 中的关系配置）。
        var file = await db.ContentFiles
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.RelativePath == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (file is null)
        {
            return;
        }

        db.ContentFiles.Remove(file);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
