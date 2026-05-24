namespace Bocchi.HomeServer.Data;

/// <summary>
/// 发布方案记录。发布方案把“发布到哪里”和“怎么发布”保存成可命名的配置，
/// 让后台首页可以优先展示渠道 icon 与用户自定义显示名。
/// </summary>
public sealed class PublishPlanRecord
{
    /// <summary>数据库主键。</summary>
    public int Id { get; set; }

    /// <summary>用户在发布页看到的方案名称，例如“个人主页 GitHub Pages”。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>发布渠道稳定 key，例如 static-files、github-pages 或 cloudflare-pages。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>渠道的非敏感配置参数；JSON object 形态便于不同渠道逐步扩展。</summary>
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>受 Data Protection 保护后的渠道凭据 JSON；不得写入 workspace 或静态输出目录。</summary>
    public string? ProtectedCredentialJson { get; set; }

    /// <summary>复用的 Git 账号连接；为空时使用发布方案自己的受保护凭据。</summary>
    public int? GitProviderConnectionId { get; set; }

    /// <summary>是否为发布页的一键发布默认方案。</summary>
    public bool IsDefault { get; set; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>最后更新时间。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
