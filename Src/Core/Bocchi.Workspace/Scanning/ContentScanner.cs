using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Bocchi.ContentModel;
using Bocchi.Workspace.Content;
using Bocchi.Workspace.Content.Loaders;
using Bocchi.Workspace.Git;
using Bocchi.Workspace.State;

using Microsoft.Extensions.Logging;

namespace Bocchi.Workspace.Scanning;

/// <summary>
/// 内容扫描器：枚举内容空间，调用各 Loader，把结果写入 <see cref="IContentStateStore"/>，并产出 <see cref="ScanResult"/>。
/// </summary>
public sealed partial class ContentScanner
{
    private static readonly string[] SuspiciousDerivativeExtensions =
    [
        ".webp", ".thumb.jpg", ".thumb.png", ".preview.jpg", ".preview.png",
    ];

    private readonly WorkspaceLayout _layout;
    private readonly IContentStateStore _store;
    private readonly IContentRepository _repository;
    private readonly PostLoader _postLoader;
    private readonly PageLoader _pageLoader;
    private readonly WorkLoader _workLoader;
    private readonly NoteLoader _noteLoader;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ContentScanner> _logger;

    public ContentScanner(
        WorkspaceLayout layout,
        IContentStateStore store,
        IContentRepository repository,
        PostLoader postLoader,
        PageLoader pageLoader,
        WorkLoader workLoader,
        NoteLoader noteLoader,
        TimeProvider timeProvider,
        ILogger<ContentScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(postLoader);
        ArgumentNullException.ThrowIfNull(pageLoader);
        ArgumentNullException.ThrowIfNull(workLoader);
        ArgumentNullException.ThrowIfNull(noteLoader);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _layout = layout;
        _store = store;
        _repository = repository;
        _postLoader = postLoader;
        _pageLoader = pageLoader;
        _workLoader = workLoader;
        _noteLoader = noteLoader;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = _timeProvider.GetUtcNow();
        string? gitHead = null;
        try
        {
            var status = await _repository.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            gitHead = status.HeadCommitSha;
        }
        catch (Exception ex)
        {
            LogGitStatusFailed(_logger, ex);
        }

        var scanRunId = await _store.StartScanRunAsync(startedAt, gitHead, cancellationToken).ConfigureAwait(false);

        var errors = new List<ContentValidationError>();
        var warnings = new List<ContentValidationError>();
        var infos = new List<ContentValidationError>();
        var posts = new List<PostDocument>();
        var pages = new List<PageDocument>();
        var works = new List<WorkDocument>();
        var notes = new List<NoteDocument>();
        IReadOnlyList<FriendLink> friendLinks = [];
        SiteSettings? siteSettings = null;
        var filesScanned = 0;

        var cs = _layout.ContentSpace;

        try
        {
            // SiteSettings 必须先解析，以便拿到回退时区
            var fallbackOffset = TimeSpan.FromHours(8); // Asia/Shanghai 默认
            (siteSettings, var siteErrors, var siteFiles) = await LoadSiteSettingsAsync(cs, cancellationToken).ConfigureAwait(false);
            errors.AddRange(siteErrors.Where(e => e.Severity == ContentErrorSeverity.Error));
            warnings.AddRange(siteErrors.Where(e => e.Severity == ContentErrorSeverity.Warning));
            infos.AddRange(siteErrors.Where(e => e.Severity == ContentErrorSeverity.Info));
            filesScanned += siteFiles;
            if (siteSettings is not null)
            {
                fallbackOffset = ResolveTimeZoneOffset(siteSettings.TimeZone, warnings, cs.SiteSettingsFile);
            }

            // Posts
            filesScanned += await ScanYearScopedDirectoryAsync(
                cs.PostsDirectory, ContentKind.Post, errors,
                async (year, slug, file, location) =>
                {
                    var raw = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var result = _postLoader.Load(location, year, slug, raw, fallbackOffset);
                    DispatchErrors(result.Errors, errors, warnings, infos);
                    if (result.Document is not null)
                    {
                        posts.Add(result.Document);
                        await PersistFileAndItemAsync(file, location, ContentKind.Post,
                            result.Document.Frontmatter.Slug, result.Document.Frontmatter.Title,
                            result.Document.Frontmatter.Status, year,
                            result.Document.Frontmatter.PublishedAt, result.Document.Frontmatter.UpdatedAt,
                            cancellationToken).ConfigureAwait(false);
                        ValidateAssets(file, location, result.Document.Body.ReferencedMedia, errors, warnings, infos);
                    }
                },
                isDirectoryItem: true,
                cancellationToken).ConfigureAwait(false);

            // Pages（无年份层）
            filesScanned += await ScanFlatDirectoryAsync(
                cs.PagesDirectory, ContentKind.Page,
                async (slug, file, location) =>
                {
                    var raw = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var result = _pageLoader.Load(location, slug, raw);
                    DispatchErrors(result.Errors, errors, warnings, infos);
                    if (result.Document is not null)
                    {
                        pages.Add(result.Document);
                        await PersistFileAndItemAsync(file, location, ContentKind.Page,
                            result.Document.Frontmatter.Slug, result.Document.Frontmatter.Title,
                            result.Document.Frontmatter.Status, year: null,
                            publishedAt: null, updatedAt: null,
                            cancellationToken).ConfigureAwait(false);
                        ValidateAssets(file, location, result.Document.Body.ReferencedMedia, errors, warnings, infos);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            // Works
            filesScanned += await ScanYearScopedDirectoryAsync(
                cs.WorksDirectory, ContentKind.Work, errors,
                async (year, slug, file, location) =>
                {
                    var raw = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    var result = _workLoader.Load(location, year, slug, raw);
                    DispatchErrors(result.Errors, errors, warnings, infos);
                    if (result.Document is not null)
                    {
                        works.Add(result.Document);
                        await PersistFileAndItemAsync(file, location, ContentKind.Work,
                            result.Document.Frontmatter.Slug, result.Document.Frontmatter.Title,
                            result.Document.Frontmatter.Status, year,
                            publishedAt: null, updatedAt: null,
                            cancellationToken).ConfigureAwait(false);
                        ValidateAssets(file, location, result.Document.Body.ReferencedMedia, errors, warnings, infos);
                    }
                },
                isDirectoryItem: true,
                cancellationToken).ConfigureAwait(false);

            // Notes（年份目录下的单文件 .md）
            filesScanned += await ScanNotesDirectoryAsync(cs, fallbackOffset, errors,
                async (year, file, location, doc, loadErrors) =>
                {
                    DispatchErrors(loadErrors, errors, warnings, infos);
                    if (doc is not null)
                    {
                        notes.Add(doc);
                        await PersistFileAndItemAsync(file, location, ContentKind.Note,
                            doc.Frontmatter.Id, title: doc.Body.Excerpt,
                            doc.Frontmatter.Status, year,
                            doc.Frontmatter.PublishedAt, updatedAt: null,
                            cancellationToken).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            // Friends
            if (File.Exists(cs.FriendsFile))
            {
                filesScanned++;
                var raw = await File.ReadAllTextAsync(cs.FriendsFile, cancellationToken).ConfigureAwait(false);
                var location = new ContentLocation(cs.Root, cs.ToRelative(cs.FriendsFile));
                var result = FriendLinksLoader.Load(location, raw);
                DispatchErrors(result.Errors, errors, warnings, infos);
                if (result.Document is not null)
                {
                    friendLinks = result.Document;
                    var fileId = await UpsertFileRecordAsync(cs.FriendsFile, location, ContentKind.FriendLink, cancellationToken).ConfigureAwait(false);
                    for (var i = 0; i < friendLinks.Count; i++)
                    {
                        var link = friendLinks[i];
                        var contentId = !string.IsNullOrWhiteSpace(link.Url)
                            ? link.Url
                            : $"friend-{i}";
                        await _store.UpsertContentItemAsync(new ContentItemUpsert(
                            ContentKind.FriendLink, contentId, Slug: null, link.Name,
                            link.Status, Year: null, PublishedAt: null, UpdatedAt: null,
                            FrontmatterJson: null, location.RelativePath), fileId, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            await _store.FinishScanRunAsync(scanRunId, _timeProvider.GetUtcNow(),
                filesScanned, posts.Count + pages.Count + works.Count + notes.Count + friendLinks.Count + (siteSettings is null ? 0 : 1),
                errors.Count, warnings.Count, "cancelled", cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await _store.FinishScanRunAsync(scanRunId, _timeProvider.GetUtcNow(),
                filesScanned, posts.Count + pages.Count + works.Count + notes.Count + friendLinks.Count + (siteSettings is null ? 0 : 1),
                errors.Count, warnings.Count, "failed", cancellationToken).ConfigureAwait(false);
            throw;
        }

        var allDiagnostics = errors.Concat(warnings).Concat(infos).ToList();
        await _store.AppendErrorsAsync(scanRunId, allDiagnostics, cancellationToken).ConfigureAwait(false);

        var itemsLoaded = posts.Count + pages.Count + works.Count + notes.Count + friendLinks.Count + (siteSettings is null ? 0 : 1);
        var finishedAt = _timeProvider.GetUtcNow();
        await _store.FinishScanRunAsync(scanRunId, finishedAt, filesScanned, itemsLoaded,
            errors.Count, warnings.Count, "succeeded", cancellationToken).ConfigureAwait(false);

        return new ScanResult(scanRunId, startedAt, finishedAt, filesScanned, itemsLoaded,
            errors, warnings, infos, posts, pages, works, notes, friendLinks, siteSettings);
    }

    private async Task<(SiteSettings? Settings, IReadOnlyList<ContentValidationError> Errors, int Files)>
        LoadSiteSettingsAsync(ContentSpaceLayout cs, CancellationToken ct)
    {
        if (!File.Exists(cs.SiteSettingsFile))
        {
            return (null, [
                new ContentValidationError(
                    cs.ToRelative(cs.SiteSettingsFile), ContentKind.SiteSettings, null,
                    ContentErrorSeverity.Warning, "SITE_FILE_MISSING",
                    "site/site.yaml 不存在。"),
            ], 0);
        }

        var siteRaw = await File.ReadAllTextAsync(cs.SiteSettingsFile, ct).ConfigureAwait(false);
        var siteLocation = new ContentLocation(cs.Root, cs.ToRelative(cs.SiteSettingsFile));
        string? navRaw = null;
        ContentLocation? navLocation = null;
        var files = 1;
        if (File.Exists(cs.NavigationFile))
        {
            navRaw = await File.ReadAllTextAsync(cs.NavigationFile, ct).ConfigureAwait(false);
            navLocation = new ContentLocation(cs.Root, cs.ToRelative(cs.NavigationFile));
            files++;
        }

        var result = SiteSettingsLoader.Load(siteLocation, siteRaw, navLocation, navRaw);
        if (result.Document is not null)
        {
            await UpsertFileRecordAsync(cs.SiteSettingsFile, siteLocation, ContentKind.SiteSettings, ct).ConfigureAwait(false);
            await _store.UpsertContentItemAsync(new ContentItemUpsert(
                ContentKind.SiteSettings, "site", Slug: null, result.Document.Title,
                ContentStatus.Published, Year: null, PublishedAt: null, UpdatedAt: null,
                FrontmatterJson: null, siteLocation.RelativePath), null, ct).ConfigureAwait(false);
        }

        return (result.Document, result.Errors, files);
    }

    private async Task<int> ScanYearScopedDirectoryAsync(
        string root,
        ContentKind kind,
        List<ContentValidationError> errors,
        Func<string, string, string, ContentLocation, Task> handle,
        bool isDirectoryItem,
        CancellationToken ct)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var cs = _layout.ContentSpace;
        var fileCount = 0;
        foreach (var yearDir in Directory.EnumerateDirectories(root))
        {
            var yearName = Path.GetFileName(yearDir);
            if (!YearRegex().IsMatch(yearName))
            {
                errors.Add(new ContentValidationError(
                    cs.ToRelative(yearDir), kind, null,
                    ContentErrorSeverity.Error, "INVALID_YEAR_DIR",
                    $"年份目录名 '{yearName}' 必须是 4 位数字。"));
                continue;
            }

            foreach (var entryDir in Directory.EnumerateDirectories(yearDir))
            {
                var slug = Path.GetFileName(entryDir);
                var indexFile = Path.Combine(entryDir, "index.md");
                if (!File.Exists(indexFile))
                {
                    errors.Add(new ContentValidationError(
                        cs.ToRelative(entryDir), kind, null,
                        ContentErrorSeverity.Error, "MISSING_INDEX",
                        $"目录 '{slug}' 缺少 index.md。"));
                    continue;
                }

                fileCount++;
                var location = new ContentLocation(cs.Root, cs.ToRelative(indexFile));
                await handle(yearName, slug, indexFile, location).ConfigureAwait(false);
            }
        }

        return fileCount;
    }

    private async Task<int> ScanFlatDirectoryAsync(
        string root, ContentKind kind, Func<string, string, ContentLocation, Task> handle, CancellationToken ct)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var cs = _layout.ContentSpace;
        var fileCount = 0;
        foreach (var entryDir in Directory.EnumerateDirectories(root))
        {
            var slug = Path.GetFileName(entryDir);
            var indexFile = Path.Combine(entryDir, "index.md");
            if (!File.Exists(indexFile))
            {
                continue;
            }

            fileCount++;
            var location = new ContentLocation(cs.Root, cs.ToRelative(indexFile));
            await handle(slug, indexFile, location).ConfigureAwait(false);
        }

        return fileCount;
    }

    private async Task<int> ScanNotesDirectoryAsync(
        ContentSpaceLayout cs,
        TimeSpan fallbackOffset,
        List<ContentValidationError> errors,
        Func<string, string, ContentLocation, NoteDocument?, IReadOnlyList<ContentValidationError>, Task> handle,
        CancellationToken ct)
    {
        if (!Directory.Exists(cs.NotesDirectory))
        {
            return 0;
        }

        var fileCount = 0;
        foreach (var yearDir in Directory.EnumerateDirectories(cs.NotesDirectory))
        {
            var yearName = Path.GetFileName(yearDir);
            if (!YearRegex().IsMatch(yearName))
            {
                errors.Add(new ContentValidationError(
                    cs.ToRelative(yearDir), ContentKind.Note, null,
                    ContentErrorSeverity.Error, "INVALID_YEAR_DIR",
                    $"短文年份目录名 '{yearName}' 必须是 4 位数字。"));
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(yearDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                fileCount++;
                var raw = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var location = new ContentLocation(cs.Root, cs.ToRelative(file));
                var fileName = Path.GetFileName(file);
                var result = _noteLoader.Load(location, yearName, fileName, raw, fallbackOffset);
                await handle(yearName, file, location, result.Document, result.Errors).ConfigureAwait(false);
            }
        }

        return fileCount;
    }

    private static void ValidateAssets(
        string sourceFile,
        ContentLocation location,
        IReadOnlyList<ContentModel.MediaReference> referenced,
        List<ContentValidationError> errors,
        List<ContentValidationError> warnings,
        List<ContentValidationError> infos)
    {
        var dir = Path.GetDirectoryName(sourceFile);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        var assetsDir = Path.Combine(dir, "assets");
        var existingAssets = Directory.Exists(assetsDir)
            ? new HashSet<string>(
                Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories),
                StringComparer.OrdinalIgnoreCase)
            : [];

        // 引用是否存在
        var referencedAbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var media in referenced)
        {
            // 跳过绝对 URL
            if (Uri.TryCreate(media.Path, UriKind.Absolute, out var u)
                && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            {
                continue;
            }

            var resolved = Path.GetFullPath(Path.Combine(dir, media.Path));
            referencedAbs.Add(resolved);
            if (!File.Exists(resolved))
            {
                errors.Add(new ContentValidationError(
                    location.RelativePath, null, "media",
                    ContentErrorSeverity.Error, "MEDIA_MISSING",
                    $"引用的媒体文件不存在：{media.Path}"));
            }
        }

        // 孤儿媒体（assets/ 中存在但未被引用）
        foreach (var asset in existingAssets)
        {
            if (!referencedAbs.Contains(asset))
            {
                infos.Add(new ContentValidationError(
                    location.RelativePath, null, "assets",
                    ContentErrorSeverity.Info, "ORPHAN_ASSET",
                    $"assets 中的资源未在内容中被引用：{Path.GetRelativePath(dir, asset)}"));
            }

            // 派生产物
            var name = Path.GetFileName(asset);
            foreach (var suspicious in SuspiciousDerivativeExtensions)
            {
                if (name.EndsWith(suspicious, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(new ContentValidationError(
                        location.RelativePath, null, "assets",
                        ContentErrorSeverity.Warning, "SUSPICIOUS_DERIVATIVE",
                        $"assets 中疑似派生产物（应由构建系统生成，不入内容空间）：{Path.GetRelativePath(dir, asset)}"));
                    break;
                }
            }
        }
    }

    private async Task PersistFileAndItemAsync(
        string filePath, ContentLocation location, ContentKind kind,
        string contentId, string? title, ContentStatus status, string? year,
        DateTimeOffset? publishedAt, DateTimeOffset? updatedAt, CancellationToken ct)
    {
        var fileId = await UpsertFileRecordAsync(filePath, location, kind, ct).ConfigureAwait(false);
        await _store.UpsertContentItemAsync(new ContentItemUpsert(
            kind, contentId, Slug: contentId, title, status, year, publishedAt, updatedAt,
            FrontmatterJson: null, location.RelativePath), fileId, ct).ConfigureAwait(false);
    }

    private async Task<long> UpsertFileRecordAsync(string filePath, ContentLocation location, ContentKind kind, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        var hash = await HashFileAsync(filePath, ct).ConfigureAwait(false);
        return await _store.UpsertFileAsync(new FileUpsert(
            location.RelativePath, kind, hash, info.LastWriteTimeUtc), ct).ConfigureAwait(false);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var bytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(bytes);
    }

    private static void DispatchErrors(
        IReadOnlyList<ContentValidationError> source,
        List<ContentValidationError> errors,
        List<ContentValidationError> warnings,
        List<ContentValidationError> infos)
    {
        foreach (var e in source)
        {
            switch (e.Severity)
            {
                case ContentErrorSeverity.Error: errors.Add(e); break;
                case ContentErrorSeverity.Warning: warnings.Add(e); break;
                case ContentErrorSeverity.Info: infos.Add(e); break;
            }
        }
    }

    private TimeSpan ResolveTimeZoneOffset(string ianaOrId, List<ContentValidationError> warnings, string siteFile)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(ianaOrId);
            return tz.GetUtcOffset(_timeProvider.GetUtcNow());
        }
        catch (TimeZoneNotFoundException)
        {
            warnings.Add(new ContentValidationError(
                Path.GetFileName(siteFile), ContentKind.SiteSettings, "timeZone",
                ContentErrorSeverity.Warning, "TZ_UNKNOWN",
                $"无法识别 timeZone='{ianaOrId}'，回退到 +08:00。"));
            return TimeSpan.FromHours(8);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeSpan.FromHours(8);
        }
    }

    [GeneratedRegex(@"^\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "无法读取内容空间 Git 状态，将忽略 Git 信息继续扫描。")]
    private static partial void LogGitStatusFailed(ILogger logger, Exception exception);
}