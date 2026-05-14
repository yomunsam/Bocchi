using Microsoft.AspNetCore.Identity;

namespace Bocchi.HomeServer.Data;

/// <summary>
/// Bocchi Home Server 的 Identity 用户。
/// </summary>
public sealed class BocchiUser : IdentityUser
{
    /// <summary>用户在后台中显示的友好名称。</summary>
    public string? DisplayName { get; set; }

    /// <summary>用户创建时间，使用 UTC 保存，避免本地时区迁移造成歧义。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最近一次本地登录成功时间，外部登录接入后同样更新。</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>禁用后不允许登录；不删除用户是为了保留审计和外部登录绑定关系。</summary>
    public bool IsDisabled { get; set; }
}
