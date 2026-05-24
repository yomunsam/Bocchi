namespace Bocchi.HomeServer.Data;

/// <summary>
/// GitHub 集成设置。它描述 Bocchi 在 GitHub 侧同一个 OAuth App 的注册信息，
/// GitHub 登录和发布 Device Flow 都从这里读取 client id。
/// </summary>
public sealed class GitHubIntegrationSettings
{
    /// <summary>固定主键，单站点只保留一份 GitHub 集成设置。</summary>
    public int Id { get; set; } = 1;

    /// <summary>登录页显示名称。</summary>
    public string DisplayName { get; set; } = "GitHub";

    /// <summary>是否启用 GitHub 登录；发布 Device Flow 只要求 OAuthClientId 存在。</summary>
    public bool LoginEnabled { get; set; }

    /// <summary>GitHub OAuth App 的 public client id。</summary>
    public string? OAuthClientId { get; set; }

    /// <summary>受 Data Protection 保护后的 GitHub OAuth App client secret；仅登录 Web Flow 使用。</summary>
    public string? ProtectedOAuthClientSecret { get; set; }

    /// <summary>GitHub 登录 callback path，默认 <c>/signin-github</c>。</summary>
    public string CallbackPath { get; set; } = "/signin-github";

    /// <summary>最后更新时间，便于设置页给用户反馈。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
