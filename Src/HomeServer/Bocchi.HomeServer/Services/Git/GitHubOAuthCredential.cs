using System.Text.Json;

namespace Bocchi.HomeServer.Services.Git;

/// <summary>GitHub OAuth Device Flow 取得的短凭据快照；保存前会被 Data Protection 保护。</summary>
public sealed record GitHubOAuthCredential
{
    /// <summary>凭据 JSON 序列化选项。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>OAuth access token。</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>GitHub 返回的 token type，通常是 bearer。</summary>
    public string TokenType { get; init; } = "bearer";

    /// <summary>授权 scope 快照。</summary>
    public string Scope { get; init; } = string.Empty;

    /// <summary>授权账号 login。</summary>
    public string GitHubLogin { get; init; } = string.Empty;

    /// <summary>授权完成时间。</summary>
    public DateTimeOffset AuthorizedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>从 JSON 读取凭据。</summary>
    public static GitHubOAuthCredential FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new GitHubOAuthCredential();
        }

        return JsonSerializer.Deserialize<GitHubOAuthCredential>(json, JsonOptions)
            ?? new GitHubOAuthCredential();
    }

    /// <summary>序列化为受保护连接凭据 JSON。</summary>
    public string ToJson()
        => JsonSerializer.Serialize(this, JsonOptions);
}
