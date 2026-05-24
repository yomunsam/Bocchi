using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Bocchi.HomeServer.Services.Publishing;

using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Services.Git;

/// <summary>
/// GitHub Device Flow 与轻量 repository API。它只负责授权和目标仓库探测；
/// 发布 commit 仍由 GitHubPagesPublisher 执行。
/// </summary>
public sealed class GitHubDeviceFlowService
{
    /// <summary>GitHub Web endpoint 根地址。</summary>
    private const string WebBase = "https://github.com";

    /// <summary>GitHub REST API 根地址。</summary>
    private const string ApiBase = "https://api.github.com";

    /// <summary>当前使用的 GitHub REST API 版本。</summary>
    private const string GitHubApiVersion = "2022-11-28";

    /// <summary>发布 branch 归属标记文件。</summary>
    public const string BocchiPublishMarkerPath = ".bocchi-publish.json";

    /// <summary>GitHub JSON 序列化选项。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>GitHub HTTP client。</summary>
    private readonly HttpClient _http;

    /// <summary>Device Flow 配置。</summary>
    private readonly GitHubDeviceFlowOptions _options;

    /// <summary>GitHub 集成设置；登录与发布 Device Flow 共用同一个 OAuth App client id。</summary>
    private readonly GitHubIntegrationSettingsService _settings;

    /// <summary>构造 GitHub Device Flow 服务。</summary>
    public GitHubDeviceFlowService(HttpClient http, IOptions<GitHubDeviceFlowOptions> options, GitHubIntegrationSettingsService settings)
    {
        _http = http;
        _options = options.Value;
        _settings = settings;
    }

    /// <summary>当前是否配置了 OAuth client id；优先读取 Dashboard 保存值，其次读取应用配置。</summary>
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
        => !string.IsNullOrWhiteSpace(await GetConfiguredClientIdAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>读取当前有效的 GitHub OAuth App client id；该值是公开 id，不是 secret。</summary>
    public async Task<string?> GetConfiguredClientIdAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        return TrimToNull(settings.OAuthClientId) ?? TrimToNull(_options.OAuthClientId);
    }

    /// <summary>发起 GitHub Device Flow，返回用户需要打开的 URL 和验证码。</summary>
    public async Task<GitHubDeviceFlowStart> StartAsync(CancellationToken cancellationToken = default)
    {
        var clientId = await RequireClientIdAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendWebFormAsync(
                "/login/device/code",
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scope"] = "repo read:user",
                },
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var dto = await ReadJsonAsync<GitHubDeviceCodeResponse>(response, cancellationToken).ConfigureAwait(false);
        return new GitHubDeviceFlowStart(
            dto.DeviceCode,
            dto.UserCode,
            dto.VerificationUri,
            dto.ExpiresIn,
            dto.Interval <= 0 ? 5 : dto.Interval);
    }

    /// <summary>轮询 GitHub Device Flow 授权结果。</summary>
    public async Task<GitHubDeviceFlowPollResult> PollAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);

        var clientId = await RequireClientIdAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendWebFormAsync(
                "/login/oauth/access_token",
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["device_code"] = deviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                },
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var dto = await ReadJsonAsync<GitHubTokenResponse>(response, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(dto.Error))
        {
            return new GitHubDeviceFlowPollResult(
                GitHubDeviceFlowPollStatusExtensions.FromGitHubError(dto.Error),
                null,
                dto.ErrorDescription,
                dto.Error == "slow_down");
        }

        var token = new GitHubOAuthCredential
        {
            AccessToken = dto.AccessToken,
            TokenType = dto.TokenType,
            Scope = dto.Scope,
            AuthorizedAtUtc = DateTimeOffset.UtcNow,
        };
        return new GitHubDeviceFlowPollResult(GitHubDeviceFlowPollStatus.Succeeded, token, null, false);
    }

    /// <summary>读取当前授权 GitHub 用户。</summary>
    public async Task<GitHubUser> GetCurrentUserAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var dto = await SendApiJsonAsync<GitHubUserResponse>(HttpMethod.Get, "/user", accessToken, null, cancellationToken)
            .ConfigureAwait(false);
        return new GitHubUser(dto.Login);
    }

    /// <summary>列出当前账号可访问的 repository，用于后续 provider picker。</summary>
    public async Task<IReadOnlyList<GitHubRepository>> ListRepositoriesAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var repos = await SendApiJsonAsync<GitHubRepositoryResponse[]>(
                HttpMethod.Get,
                "/user/repos?affiliation=owner,collaborator&sort=updated&per_page=100",
                accessToken,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        return repos
            .Select(x => new GitHubRepository(x.Owner.Login, x.Name, x.FullName, x.HtmlUrl, x.Private))
            .ToArray();
    }

    /// <summary>创建发布专用 repository；发布 repo 允许 auto_init，避免空仓库无法创建 refs。</summary>
    public async Task<GitHubRepository> CreateRepositoryAsync(
        string accessToken,
        string name,
        bool privateRepository,
        bool autoInit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var dto = await SendApiJsonAsync<GitHubRepositoryResponse>(
                HttpMethod.Post,
                "/user/repos",
                accessToken,
                new
                {
                    name = name.Trim(),
                    @private = privateRepository,
                    auto_init = autoInit,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return new GitHubRepository(dto.Owner.Login, dto.Name, dto.FullName, dto.HtmlUrl, dto.Private);
    }

    /// <summary>检查 GitHub Pages 发布 branch 是否可由 Bocchi 安全接管。</summary>
    public async Task<GitHubPublishBranchCheck> CheckPublishBranchAsync(
        string accessToken,
        string owner,
        string repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);

        var ownerSegment = EscapeSegment(owner);
        var repoSegment = EscapeSegment(repository);
        var branchRef = "heads/" + EscapeRefPath(branch);
        var reference = await TryGetReferenceAsync(ownerSegment, repoSegment, branchRef, accessToken, cancellationToken).ConfigureAwait(false);
        if (reference is null)
        {
            return new GitHubPublishBranchCheck(GitHubPublishBranchState.Missing, false, 0);
        }

        var commit = await SendApiJsonAsync<GitHubCommitResponse>(
                HttpMethod.Get,
                $"/repos/{ownerSegment}/{repoSegment}/git/commits/{EscapeSegment(reference.Object.Sha)}",
                accessToken,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var tree = await SendApiJsonAsync<GitHubTreeResponse>(
                HttpMethod.Get,
                $"/repos/{ownerSegment}/{repoSegment}/git/trees/{EscapeSegment(commit.Tree.Sha)}?recursive=1",
                accessToken,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        var files = tree.Tree.Count(x => string.Equals(x.Type, "blob", StringComparison.Ordinal));
        var hasMarker = tree.Tree.Any(x => string.Equals(x.Path, BocchiPublishMarkerPath, StringComparison.Ordinal));
        var state = hasMarker
            ? GitHubPublishBranchState.ManagedByBocchi
            : (files == 0 ? GitHubPublishBranchState.Empty : GitHubPublishBranchState.Occupied);
        return new GitHubPublishBranchCheck(state, hasMarker, files);
    }

    private async Task<string> RequireClientIdAsync(CancellationToken cancellationToken)
    {
        var clientId = await GetConfiguredClientIdAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(clientId)
            ? throw new InvalidOperationException("GitHub OAuth client id is not configured.")
            : clientId;
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<GitHubReferenceResponse?> TryGetReferenceAsync(
        string owner,
        string repo,
        string branchRef,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var response = await SendApiAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/git/ref/{branchRef}", accessToken, null, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken, accessToken).ConfigureAwait(false);
        return await ReadJsonAsync<GitHubReferenceResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendApiJsonAsync<T>(
        HttpMethod method,
        string path,
        string accessToken,
        object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendApiAsync(method, path, accessToken, body, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken, accessToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendApiAsync(
        HttpMethod method,
        string path,
        string accessToken,
        object? body,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, new Uri(ApiBase + path));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.UserAgent.ParseAdd("Bocchi-HomeServer");
        message.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendWebFormAsync(
        string path,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(WebBase + path));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.UserAgent.ParseAdd("Bocchi-HomeServer");
        message.Content = new FormUrlEncodedContent(values);
        return await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string? secret = null)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var sanitized = PublishSecretSanitizer.Sanitize(message, secret);
        throw new PublishTargetException($"GitHub API 返回 {(int)response.StatusCode} {response.ReasonPhrase}: {sanitized}");
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new PublishTargetException("GitHub API 返回了空响应。");
    }

    private static string EscapeSegment(string value)
        => Uri.EscapeDataString(value.Trim());

    private static string EscapeRefPath(string value)
        => string.Join('/', value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Uri.EscapeDataString));

    private sealed record GitHubDeviceCodeResponse(
        [property: JsonPropertyName("device_code")] string DeviceCode,
        [property: JsonPropertyName("user_code")] string UserCode,
        [property: JsonPropertyName("verification_uri")] string VerificationUri,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int Interval);

    private sealed record GitHubTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("scope")] string Scope,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);

    private sealed record GitHubUserResponse(string Login);

    private sealed record GitHubRepositoryResponse(
        string Name,
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        bool Private,
        GitHubRepositoryOwnerResponse Owner);

    private sealed record GitHubRepositoryOwnerResponse(string Login);

    private sealed record GitHubReferenceResponse(GitHubReferenceObjectResponse Object);

    private sealed record GitHubReferenceObjectResponse(string Sha);

    private sealed record GitHubCommitResponse(GitHubCommitTreeResponse Tree);

    private sealed record GitHubCommitTreeResponse(string Sha);

    private sealed record GitHubTreeResponse(IReadOnlyList<GitHubTreeEntryResponse> Tree);

    private sealed record GitHubTreeEntryResponse(string Path, string Type);
}

/// <summary>Device Flow 发起结果。</summary>
public sealed record GitHubDeviceFlowStart(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

/// <summary>Device Flow 轮询结果。</summary>
public sealed record GitHubDeviceFlowPollResult(
    GitHubDeviceFlowPollStatus Status,
    GitHubOAuthCredential? Credential,
    string? ErrorDescription,
    bool IncreasePollingInterval);

/// <summary>Device Flow 轮询状态。</summary>
public enum GitHubDeviceFlowPollStatus
{
    /// <summary>授权已完成。</summary>
    Succeeded,

    /// <summary>用户尚未完成授权。</summary>
    Pending,

    /// <summary>GitHub 要求降低轮询频率。</summary>
    SlowDown,

    /// <summary>用户拒绝授权。</summary>
    AccessDenied,

    /// <summary>验证码已经过期。</summary>
    Expired,

    /// <summary>GitHub 返回了未知错误。</summary>
    Failed,
}

/// <summary>GitHub Device Flow 状态转换。</summary>
public static class GitHubDeviceFlowPollStatusExtensions
{
    /// <summary>把 GitHub error code 映射到内部状态。</summary>
    public static GitHubDeviceFlowPollStatus FromGitHubError(string error)
        => error switch
        {
            "authorization_pending" => GitHubDeviceFlowPollStatus.Pending,
            "slow_down" => GitHubDeviceFlowPollStatus.SlowDown,
            "access_denied" => GitHubDeviceFlowPollStatus.AccessDenied,
            "expired_token" => GitHubDeviceFlowPollStatus.Expired,
            _ => GitHubDeviceFlowPollStatus.Failed,
        };
}

/// <summary>GitHub 用户摘要。</summary>
public sealed record GitHubUser(string Login);

/// <summary>GitHub repository 摘要。</summary>
public sealed record GitHubRepository(string Owner, string Name, string FullName, string HtmlUrl, bool Private);

/// <summary>发布 branch 检查结果。</summary>
public sealed record GitHubPublishBranchCheck(GitHubPublishBranchState State, bool HasBocchiMarker, int FileCount);

/// <summary>发布 branch 安全状态。</summary>
public enum GitHubPublishBranchState
{
    /// <summary>Branch 不存在，可以由发布器创建。</summary>
    Missing,

    /// <summary>Branch 存在但没有文件。</summary>
    Empty,

    /// <summary>Branch 已经带有 Bocchi marker，可以继续精确发布。</summary>
    ManagedByBocchi,

    /// <summary>Branch 有内容但没有 Bocchi marker，需要阻止或显式接管。</summary>
    Occupied,
}
