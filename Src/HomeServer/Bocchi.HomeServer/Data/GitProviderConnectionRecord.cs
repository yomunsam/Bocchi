namespace Bocchi.HomeServer.Data;

/// <summary>
/// Git provider 账号连接。它只保存授权关系和受保护凭据；
/// 内容 remote 与发布方案分别引用它，避免把 token 写进 workspace 或 remote URL。
/// </summary>
public sealed class GitProviderConnectionRecord
{
    /// <summary>数据库主键。</summary>
    public int Id { get; set; }

    /// <summary>Provider 稳定 key，例如 github、gitlab、gitea 或 generic。</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Provider API 或实例根地址；GitHub.com 使用 https://github.com。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>授权账号的显示登录名；Generic Git 可为空。</summary>
    public string AccountLogin { get; set; } = string.Empty;

    /// <summary>授权 scope 快照，用空格分隔，便于后台判断能力边界。</summary>
    public string Scopes { get; set; } = string.Empty;

    /// <summary>受 Data Protection 保护后的凭据 JSON。</summary>
    public string ProtectedCredentialJson { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
