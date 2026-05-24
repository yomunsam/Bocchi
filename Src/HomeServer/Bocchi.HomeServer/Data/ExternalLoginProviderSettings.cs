namespace Bocchi.HomeServer.Data;

/// <summary>
/// 第三方登录 Provider 配置。GitHub 已收敛到 GitHubIntegrationSettings；
/// 这里保留通用 OIDC 等可配置 Provider，避免把 Logto 等具体服务写死进模型。
/// </summary>
public sealed class ExternalLoginProviderSettings
{
    /// <summary>数据库主键。</summary>
    public int Id { get; set; }

    /// <summary>稳定 Provider key，例如 github 或 oidc。</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>登录页显示名称。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>是否启用。启用但配置不完整时，登录页仍不会展示按钮。</summary>
    public bool Enabled { get; set; }

    /// <summary>OAuth / OIDC client id。</summary>
    public string? ClientId { get; set; }

    /// <summary>受 Data Protection 保护后的 client secret。</summary>
    public string? ProtectedClientSecret { get; set; }

    /// <summary>回调路径，例如 /signin-github 或 /signin-oidc-custom。</summary>
    public string? CallbackPath { get; set; }

    /// <summary>OIDC authority。GitHub Provider 不使用该字段。</summary>
    public string? Authority { get; set; }

    /// <summary>OIDC response type，默认 code。</summary>
    public string ResponseType { get; set; } = "code";

    /// <summary>OIDC 是否使用 PKCE。</summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>Scope 列表，使用空格分隔，默认 openid profile email。</summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>OIDC name claim type，可为空以使用 Provider 默认值。</summary>
    public string? NameClaimType { get; set; }

    /// <summary>OIDC email claim type，可为空以使用 Provider 默认值。</summary>
    public string? EmailClaimType { get; set; }

    /// <summary>最后更新时间，便于设置页显示保存反馈。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>判断登录页是否可以展示该 Provider 按钮。</summary>
    public bool IsReadyForLogin()
    {
        if (!Enabled || string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ProtectedClientSecret))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(Authority);
    }
}
