using System.Text.Json;

using Bocchi.HomeServer.Data;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Bocchi.HomeServer.Services.Git;

/// <summary>
/// Git provider 连接服务。它统一保存账号授权和受保护凭据；
/// workspace remote 与发布方案只引用连接 id，不直接携带明文 token。
/// </summary>
public sealed class GitProviderConnectionService
{
    /// <summary>连接凭据 JSON 的统一序列化选项。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Home Server 状态数据库。</summary>
    private readonly BocchiDbContext _db;

    /// <summary>连接凭据保护器。</summary>
    private readonly IDataProtector _protector;

    /// <summary>时间来源，测试可替换。</summary>
    private readonly TimeProvider _time;

    /// <summary>构造 Git provider 连接服务。</summary>
    public GitProviderConnectionService(BocchiDbContext db, IDataProtectionProvider protection, TimeProvider time)
    {
        _db = db;
        _protector = protection.CreateProtector("Bocchi.HomeServer.GitProviderCredentials.v1");
        _time = time;
    }

    /// <summary>列出全部连接，最近更新的账号排在前面。</summary>
    public async Task<IReadOnlyList<GitProviderConnectionRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var records = await _db.GitProviderConnections
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return records
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.Id)
            .ToArray();
    }

    /// <summary>按 id 读取连接。</summary>
    public async Task<GitProviderConnectionRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
        => await _db.GitProviderConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>读取指定 provider 的最近连接；向导用它恢复默认账号。</summary>
    public async Task<GitProviderConnectionRecord?> GetLatestByProviderAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProvider(providerKey);
        var records = await _db.GitProviderConnections
            .AsNoTracking()
            .Where(x => x.ProviderKey == normalized)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return records
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
    }

    /// <summary>保存或更新一个 provider 连接；credentialJson 必须是 JSON object。</summary>
    public async Task<GitProviderConnectionRecord> SaveAsync(
        GitProviderConnectionSaveInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var provider = NormalizeProvider(input.ProviderKey);
        var baseUrl = NormalizeBaseUrl(input.BaseUrl, provider);
        var login = input.AccountLogin.Trim();
        var scopes = NormalizeScopes(input.Scopes);
        var protectedCredential = _protector.Protect(NormalizeJsonObject(input.CredentialJson));
        var now = _time.GetUtcNow();

        GitProviderConnectionRecord? record = null;
        if (input.Id is { } id && id > 0)
        {
            record = await _db.GitProviderConnections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken).ConfigureAwait(false);
        }

        if (record is null)
        {
            record = new GitProviderConnectionRecord { CreatedAt = now };
            _db.GitProviderConnections.Add(record);
        }

        record.ProviderKey = provider;
        record.BaseUrl = baseUrl;
        record.AccountLogin = login;
        record.Scopes = scopes;
        record.ProtectedCredentialJson = protectedCredential;
        record.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <summary>读取受保护凭据明文；只在 Git 操作或发布操作前短暂使用。</summary>
    public string UnprotectCredentialJson(GitProviderConnectionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _protector.Unprotect(record.ProtectedCredentialJson);
    }

    /// <summary>读取指定连接的凭据明文；不存在时返回 null。</summary>
    public async Task<string?> TryUnprotectCredentialJsonAsync(int? id, CancellationToken cancellationToken = default)
    {
        if (id is null)
        {
            return null;
        }

        var record = await _db.GitProviderConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        return record is null ? null : UnprotectCredentialJson(record);
    }

    private static string NormalizeProvider(string? providerKey)
    {
        var normalized = providerKey?.Trim().ToLowerInvariant();
        return normalized switch
        {
            GitProviderKeys.GitHub => GitProviderKeys.GitHub,
            GitProviderKeys.GitLab => GitProviderKeys.GitLab,
            GitProviderKeys.Gitea => GitProviderKeys.Gitea,
            GitProviderKeys.Generic => GitProviderKeys.Generic,
            _ => throw new ArgumentException($"Unsupported Git provider: {providerKey}", nameof(providerKey)),
        };
    }

    private static string NormalizeBaseUrl(string? baseUrl, string provider)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl) && provider == GitProviderKeys.GitHub
            ? "https://github.com"
            : baseUrl?.Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Git provider base URL is required.", nameof(baseUrl))
            : value;
    }

    private static string NormalizeScopes(string? scopes)
        => string.Join(
            ' ',
            (scopes ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Order(StringComparer.Ordinal));

    private static string NormalizeJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Git provider credential JSON must be an object.", nameof(json));
        }

        return JsonSerializer.Serialize(document.RootElement, JsonOptions);
    }
}

/// <summary>保存 Git provider 连接所需的输入模型。</summary>
public sealed record GitProviderConnectionSaveInput(
    int? Id,
    string ProviderKey,
    string BaseUrl,
    string AccountLogin,
    string Scopes,
    string CredentialJson);
