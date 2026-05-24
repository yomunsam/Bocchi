using Bocchi.HomeServer.Data;
using Bocchi.Workspace;
using Bocchi.Workspace.Exceptions;
using Bocchi.Workspace.Git;

using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services.Git;

/// <summary>
/// 内容 workspace remote 服务。它把数据库中的 remote 配置与 LibGit2 操作串起来，
/// 并负责导入已有仓库前的 DataRoot/state 备份。
/// </summary>
public sealed class ContentWorkspaceRemoteService
{
    /// <summary>Home Server 状态数据库。</summary>
    private readonly BocchiDbContext _db;

    /// <summary>内容 workspace Git 仓库。</summary>
    private readonly IContentRepository _repository;

    /// <summary>DataRoot 布局，用于备份导入前的 workspace。</summary>
    private readonly BocchiDataLayout _layout;

    /// <summary>Git provider 连接服务。</summary>
    private readonly GitProviderConnectionService _connections;

    /// <summary>时间来源，测试可替换。</summary>
    private readonly TimeProvider _time;

    /// <summary>构造内容 workspace remote 服务。</summary>
    public ContentWorkspaceRemoteService(
        BocchiDbContext db,
        IContentRepository repository,
        BocchiDataLayout layout,
        GitProviderConnectionService connections,
        TimeProvider time)
    {
        _db = db;
        _repository = repository;
        _layout = layout;
        _connections = connections;
        _time = time;
    }

    /// <summary>列出已保存的内容 remote。</summary>
    public async Task<IReadOnlyList<ContentWorkspaceRemoteRecord>> ListAsync(CancellationToken cancellationToken = default)
        => await _db.ContentWorkspaceRemotes
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>保存内容 remote 配置；不会触发 push/pull。</summary>
    public async Task<ContentWorkspaceRemoteRecord> SaveAsync(
        ContentWorkspaceRemoteSaveInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var settings = Normalize(input);
        var now = _time.GetUtcNow();

        ContentWorkspaceRemoteRecord? record = null;
        if (input.Id is { } id && id > 0)
        {
            record = await _db.ContentWorkspaceRemotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        }

        if (record is null)
        {
            record = new ContentWorkspaceRemoteRecord { CreatedAt = now };
            _db.ContentWorkspaceRemotes.Add(record);
        }

        record.RemoteName = settings.RemoteName;
        record.RemoteUrl = settings.RemoteUrl;
        record.Branch = settings.Branch;
        record.GitProviderConnectionId = input.GitProviderConnectionId;
        record.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <summary>配置本地 Git remote，并保存记录。</summary>
    public async Task<ContentWorkspaceRemoteRecord> ConnectAsync(
        ContentWorkspaceRemoteSaveInput input,
        CancellationToken cancellationToken = default)
    {
        var record = await SaveAsync(input, cancellationToken).ConfigureAwait(false);
        await _repository.ConfigureRemoteAsync(ToSettings(record), cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <summary>推送内容 workspace；失败会以脱敏状态写回记录。</summary>
    public async Task<ContentRemoteOperationResult> PushAsync(int id, CancellationToken cancellationToken = default)
    {
        var record = await LoadTrackedAsync(id, cancellationToken).ConfigureAwait(false);
        return await RunRemoteOperationAsync(
                record,
                credential => _repository.PushAsync(ToSettings(record), credential, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>执行 fast-forward pull；dirty 或分叉历史会失败。</summary>
    public async Task<ContentRemoteOperationResult> PullFastForwardAsync(int id, CancellationToken cancellationToken = default)
    {
        var record = await LoadTrackedAsync(id, cancellationToken).ConfigureAwait(false);
        return await RunRemoteOperationAsync(
                record,
                credential => _repository.PullFastForwardAsync(ToSettings(record), credential, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>导入已有内容仓库：先备份当前 workspace，再 clone 远端内容。</summary>
    public async Task<ContentRemoteOperationResult> ImportExistingAsync(int id, CancellationToken cancellationToken = default)
    {
        var record = await LoadTrackedAsync(id, cancellationToken).ConfigureAwait(false);
        var credential = await ResolveCredentialAsync(record.GitProviderConnectionId, cancellationToken).ConfigureAwait(false);
        var backupPath = CreateWorkspaceBackupPath();
        var workspaceRoot = _layout.WorkspaceRoot;

        try
        {
            BackupWorkspace(workspaceRoot, backupPath);
            var result = await _repository.CloneIntoEmptyWorkspaceAsync(ToSettings(record), credential, cancellationToken).ConfigureAwait(false);
            ApplySuccess(record, result);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RestoreWorkspaceBestEffort(workspaceRoot, backupPath);
            var result = new ContentRemoteOperationResult("failed", SanitizeMessage(ex.Message), null);
            ApplyFailure(record, result);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ContentRemoteOperationResult> RunRemoteOperationAsync(
        ContentWorkspaceRemoteRecord record,
        Func<ContentRemoteCredential?, Task<ContentRemoteOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        var credential = await ResolveCredentialAsync(record.GitProviderConnectionId, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await operation(credential).ConfigureAwait(false);
            ApplySuccess(record, result);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var result = new ContentRemoteOperationResult("failed", SanitizeMessage(ex.Message), null);
            ApplyFailure(record, result);
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ContentWorkspaceRemoteRecord> LoadTrackedAsync(int id, CancellationToken cancellationToken)
        => await _db.ContentWorkspaceRemotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false)
           ?? throw new ContentGitException("内容 workspace remote 不存在。");

    private async Task<ContentRemoteCredential?> ResolveCredentialAsync(int? connectionId, CancellationToken cancellationToken)
    {
        if (connectionId is null)
        {
            return null;
        }

        var connection = await _connections.GetAsync(connectionId.Value, cancellationToken).ConfigureAwait(false)
            ?? throw new ContentGitException("Git provider 连接不存在。");
        var credentialJson = _connections.UnprotectCredentialJson(connection);
        if (connection.ProviderKey == GitProviderKeys.GitHub)
        {
            var credential = GitHubOAuthCredential.FromJson(credentialJson);
            return new ContentRemoteCredential("x-access-token", credential.AccessToken);
        }

        return null;
    }

    private static ContentRemoteSettings Normalize(ContentWorkspaceRemoteSaveInput input)
    {
        var settings = new ContentRemoteSettings(
            string.IsNullOrWhiteSpace(input.RemoteName) ? "origin" : input.RemoteName.Trim(),
            input.RemoteUrl.Trim(),
            string.IsNullOrWhiteSpace(input.Branch) ? "main" : input.Branch.Trim());
        if (Uri.TryCreate(settings.RemoteUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new ContentGitException("内容 remote URL 不能包含用户名、token 或密码。");
        }

        return settings;
    }

    private static ContentRemoteSettings ToSettings(ContentWorkspaceRemoteRecord record)
        => new(record.RemoteName, record.RemoteUrl, record.Branch);

    private void ApplySuccess(ContentWorkspaceRemoteRecord record, ContentRemoteOperationResult result)
    {
        record.LastSyncStatus = result.Status;
        record.LastSyncMessage = result.Message;
        record.LastSyncedAt = _time.GetUtcNow();
        record.UpdatedAt = record.LastSyncedAt.Value;
    }

    private void ApplyFailure(ContentWorkspaceRemoteRecord record, ContentRemoteOperationResult result)
    {
        record.LastSyncStatus = result.Status;
        record.LastSyncMessage = result.Message;
        record.LastSyncedAt = _time.GetUtcNow();
        record.UpdatedAt = record.LastSyncedAt.Value;
    }

    private string CreateWorkspaceBackupPath()
    {
        var stamp = _time.GetUtcNow().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(_layout.StateDirectory, "workspace-import-backups", stamp);
    }

    private static void BackupWorkspace(string workspaceRoot, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Move(workspaceRoot, backupPath);
        }

        Directory.CreateDirectory(workspaceRoot);
    }

    private static void RestoreWorkspaceBestEffort(string workspaceRoot, string backupPath)
    {
        try
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, workspaceRoot);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SanitizeMessage(string message)
        => string.IsNullOrWhiteSpace(message) ? "内容 workspace remote 操作失败。" : message;
}

/// <summary>保存内容 workspace remote 的输入模型。</summary>
public sealed record ContentWorkspaceRemoteSaveInput(
    int? Id,
    string RemoteName,
    string RemoteUrl,
    string Branch,
    int? GitProviderConnectionId);
