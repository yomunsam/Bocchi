namespace Bocchi.HomeServer.Services.Git;

/// <summary>GitHub Device Flow 配置。NAS 场景只需要公开 OAuth client id，不需要 client secret。</summary>
public sealed class GitHubDeviceFlowOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Bocchi:Publish:GitHub";

    /// <summary>GitHub OAuth App client id；未配置时连接向导会显示配置缺失状态。</summary>
    public string? OAuthClientId { get; set; }
}
