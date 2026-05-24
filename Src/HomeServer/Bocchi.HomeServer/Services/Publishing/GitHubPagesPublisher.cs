using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Bocchi.Generator.Pipeline;
using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>通过 GitHub REST Git Database API 把静态输出发布到 GitHub Pages branch。</summary>
public sealed class GitHubPagesPublisher : IPublishTargetPublisher
{
    /// <summary>GitHub REST API 根地址。</summary>
    private const string ApiBase = "https://api.github.com";

    /// <summary>当前使用的 GitHub REST API 版本。</summary>
    private const string GitHubApiVersion = "2022-11-28";

    /// <summary>GitHub 请求体和响应体的 JSON 命名约定。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>由 DI 注入的 HTTP client；测试可替换 handler 验证请求形状。</summary>
    private readonly HttpClient _http;

    /// <summary>构造 GitHub Pages publisher。</summary>
    public GitHubPagesPublisher(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public string Channel => PublishPlanService.GitHubPagesChannel;

    /// <inheritdoc />
    public async Task<PublishTargetResult> PublishAsync(PublishTargetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuration = GitHubPagesPublishConfiguration.FromJson(request.ConfigurationJson).Normalize();
        var credential = GitHubPagesPublishCredential.FromJson(request.CredentialJson);
        Validate(configuration, credential);

        var owner = EscapeSegment(configuration.Owner);
        var repo = EscapeSegment(configuration.Repository);
        var branchRef = "heads/" + EscapeRefPath(configuration.Branch);
        var branchUrl = $"https://github.com/{configuration.Owner}/{configuration.Repository}/tree/{configuration.Branch}";

        var existingRef = await TryGetReferenceAsync(owner, repo, branchRef, credential.Token, cancellationToken).ConfigureAwait(false);
        var blobEntries = new List<GitHubTreeEntry>(request.Output.Files.Count);
        foreach (var file in request.Output.Files)
        {
            var blobSha = await CreateBlobAsync(owner, repo, file, credential.Token, cancellationToken).ConfigureAwait(false);
            blobEntries.Add(new GitHubTreeEntry(file.RelativePath, "100644", "blob", blobSha));
        }

        var treeSha = await CreateTreeAsync(owner, repo, blobEntries, credential.Token, cancellationToken).ConfigureAwait(false);
        var commitSha = await CreateCommitAsync(
            owner,
            repo,
            treeSha,
            existingRef?.Object.Sha,
            request.BuildResult,
            credential.Token,
            cancellationToken).ConfigureAwait(false);

        if (existingRef is null)
        {
            await CreateReferenceAsync(owner, repo, configuration.Branch, commitSha, credential.Token, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UpdateReferenceAsync(owner, repo, branchRef, commitSha, credential.Token, cancellationToken).ConfigureAwait(false);
        }

        if (!configuration.EnsurePagesSource)
        {
            return new PublishTargetResult
            {
                RemoteCommitSha = commitSha,
                RemoteUrl = branchUrl,
            };
        }

        try
        {
            var pageUrl = await EnsurePagesSourceAsync(owner, repo, configuration.Branch, credential.Token, cancellationToken).ConfigureAwait(false);
            return new PublishTargetResult
            {
                RemoteCommitSha = commitSha,
                RemoteUrl = string.IsNullOrWhiteSpace(pageUrl) ? branchUrl : pageUrl,
            };
        }
        catch (PublishTargetException ex)
        {
            throw new PublishTargetException(
                $"GitHub branch 已更新到 {commitSha}，但 Pages source 配置失败：{ex.Message}",
                commitSha,
                branchUrl,
                ex);
        }
    }

    /// <summary>校验 GitHub 配置与 token，避免无意义的远端请求。</summary>
    private static void Validate(GitHubPagesPublishConfiguration configuration, GitHubPagesPublishCredential credential)
    {
        if (string.IsNullOrWhiteSpace(configuration.Owner))
        {
            throw new PublishTargetException("GitHub owner 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Repository))
        {
            throw new PublishTargetException("GitHub repository 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(configuration.Branch))
        {
            throw new PublishTargetException("GitHub Pages branch 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(credential.Token))
        {
            throw new PublishTargetException("GitHub token 不能为空。");
        }
    }

    /// <summary>读取 branch ref；404 表示首次发布到该 branch。</summary>
    private async Task<GitHubReference?> TryGetReferenceAsync(
        string owner,
        string repo,
        string branchRef,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/git/ref/{branchRef}", token, null, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, token, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<GitHubReference>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>为单个文件创建 Git blob；内容使用 base64，确保二进制文件不被 UTF-8 转码。</summary>
    private async Task<string> CreateBlobAsync(
        string owner,
        string repo,
        StaticOutputFile file,
        string token,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(file.AbsolutePath, cancellationToken).ConfigureAwait(false);
        var body = new GitHubCreateBlobRequest(Convert.ToBase64String(bytes), "base64");
        var response = await SendJsonAsync<GitHubShaResponse>(
            HttpMethod.Post,
            $"/repos/{owner}/{repo}/git/blobs",
            token,
            body,
            cancellationToken).ConfigureAwait(false);
        return response.Sha;
    }

    /// <summary>创建不带 base_tree 的完整 tree，确保远端 branch 精确等于本地 output/public。</summary>
    private async Task<string> CreateTreeAsync(
        string owner,
        string repo,
        IReadOnlyList<GitHubTreeEntry> entries,
        string token,
        CancellationToken cancellationToken)
    {
        var response = await SendJsonAsync<GitHubShaResponse>(
            HttpMethod.Post,
            $"/repos/{owner}/{repo}/git/trees",
            token,
            new { tree = entries },
            cancellationToken).ConfigureAwait(false);
        return response.Sha;
    }

    /// <summary>创建发布 commit；已有 branch 时以当前 tip 为 parent，缺失 branch 时创建 root commit。</summary>
    private async Task<string> CreateCommitAsync(
        string owner,
        string repo,
        string treeSha,
        string? parentSha,
        BuildResult buildResult,
        string token,
        CancellationToken cancellationToken)
    {
        var shortFingerprint = buildResult.Fingerprint?.Value is { Length: >= 8 } fp ? fp[..8] : buildResult.SessionId.ToString("N")[..8];
        var message = $"Publish Bocchi static site {shortFingerprint}";
        var parents = string.IsNullOrWhiteSpace(parentSha) ? Array.Empty<string>() : [parentSha];
        var response = await SendJsonAsync<GitHubShaResponse>(
            HttpMethod.Post,
            $"/repos/{owner}/{repo}/git/commits",
            token,
            new { message, tree = treeSha, parents },
            cancellationToken).ConfigureAwait(false);
        return response.Sha;
    }

    /// <summary>首次发布时创建 branch ref。</summary>
    private async Task CreateReferenceAsync(
        string owner,
        string repo,
        string branch,
        string commitSha,
        string token,
        CancellationToken cancellationToken)
        => await SendJsonWithoutResultAsync(
            HttpMethod.Post,
            $"/repos/{owner}/{repo}/git/refs",
            token,
            new { @ref = $"refs/heads/{branch}", sha = commitSha },
            cancellationToken).ConfigureAwait(false);

    /// <summary>更新已有 branch ref；默认要求 fast-forward，避免悄悄覆盖并发发布。</summary>
    private async Task UpdateReferenceAsync(
        string owner,
        string repo,
        string branchRef,
        string commitSha,
        string token,
        CancellationToken cancellationToken)
        => await SendJsonWithoutResultAsync(
            HttpMethod.Patch,
            $"/repos/{owner}/{repo}/git/refs/{branchRef}",
            token,
            new { sha = commitSha, force = false },
            cancellationToken).ConfigureAwait(false);

    /// <summary>确保 GitHub Pages 使用 branch 根目录作为 legacy source。</summary>
    private async Task<string?> EnsurePagesSourceAsync(
        string owner,
        string repo,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        var existing = await TryGetPagesSiteAsync(owner, repo, token, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            var created = await SendJsonAsync<GitHubPagesSite>(
                HttpMethod.Post,
                $"/repos/{owner}/{repo}/pages",
                token,
                CreatePagesSourceBody(branch),
                cancellationToken).ConfigureAwait(false);
            return created.HtmlUrl;
        }

        if (string.Equals(existing.Source?.Branch, branch, StringComparison.Ordinal)
            && string.Equals(existing.Source?.Path, "/", StringComparison.Ordinal))
        {
            return existing.HtmlUrl;
        }

        await SendJsonWithoutResultAsync(
            HttpMethod.Put,
            $"/repos/{owner}/{repo}/pages",
            token,
            CreatePagesSourceBody(branch),
            cancellationToken).ConfigureAwait(false);
        return existing.HtmlUrl;
    }

    /// <summary>读取 GitHub Pages 站点配置；404 表示尚未启用 Pages。</summary>
    private async Task<GitHubPagesSite?> TryGetPagesSiteAsync(string owner, string repo, string token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/repos/{owner}/{repo}/pages", token, null, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, token, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<GitHubPagesSite>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>GitHub Pages branch-source 请求体。</summary>
    private static object CreatePagesSourceBody(string branch)
        => new { build_type = "legacy", source = new { branch, path = "/" } };

    /// <summary>发送 JSON 请求并读取 JSON 响应。</summary>
    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string path,
        string token,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, token, body, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, token, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>发送 JSON 请求，只校验状态码。</summary>
    private async Task SendJsonWithoutResultAsync(
        HttpMethod method,
        string path,
        string token,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, token, body, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>创建并发送 GitHub API 请求，统一附加版本和授权 header。</summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string token,
        object? body,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, new Uri(ApiBase + path));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.UserAgent.ParseAdd("Bocchi-HomeServer");
        message.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>校验 GitHub 响应状态，失败时读取脱敏错误摘要。</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string token, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await ReadGitHubErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
        var sanitized = PublishSecretSanitizer.Sanitize(message, token);
        throw new PublishTargetException(
            $"GitHub API 返回 {(int)response.StatusCode} {response.ReasonPhrase}: {sanitized}");
    }

    /// <summary>读取 GitHub API JSON 响应。</summary>
    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new PublishTargetException("GitHub API 返回了空响应。");
    }

    /// <summary>尽量从 GitHub 错误响应中提取 message 字段。</summary>
    private static async Task<string> ReadGitHubErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "empty response";
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? raw;
            }
        }
        catch (JsonException)
        {
            return raw;
        }

        return raw;
    }

    /// <summary>转义普通 URL path segment。</summary>
    private static string EscapeSegment(string value)
        => Uri.EscapeDataString(value);

    /// <summary>转义 Git ref 中的 branch 部分，同时保留 slash 层级。</summary>
    private static string EscapeRefPath(string value)
        => string.Join('/', value.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    /// <summary>GitHub SHA 响应。</summary>
    private sealed record GitHubShaResponse
    {
        /// <summary>Git object SHA。</summary>
        public required string Sha { get; init; }
    }

    /// <summary>创建 blob 请求。</summary>
    private sealed record GitHubCreateBlobRequest(string Content, string Encoding);

    /// <summary>创建 tree 的单个 entry。</summary>
    private sealed record GitHubTreeEntry(string Path, string Mode, string Type, string Sha);

    /// <summary>Git ref 响应。</summary>
    private sealed record GitHubReference
    {
        /// <summary>ref 指向的对象。</summary>
        public required GitHubRefObject Object { get; init; }
    }

    /// <summary>Git ref object 响应。</summary>
    private sealed record GitHubRefObject
    {
        /// <summary>ref 指向的 SHA。</summary>
        public required string Sha { get; init; }
    }

    /// <summary>GitHub Pages 站点响应。</summary>
    private sealed record GitHubPagesSite
    {
        /// <summary>Pages 公开 URL。</summary>
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        /// <summary>Pages source 配置。</summary>
        public GitHubPagesSource? Source { get; init; }
    }

    /// <summary>GitHub Pages source 响应。</summary>
    private sealed record GitHubPagesSource
    {
        /// <summary>source branch。</summary>
        public string? Branch { get; init; }

        /// <summary>source path。</summary>
        public string? Path { get; init; }
    }
}
