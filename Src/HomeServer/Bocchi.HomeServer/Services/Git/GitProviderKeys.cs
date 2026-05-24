namespace Bocchi.HomeServer.Services.Git;

/// <summary>Git provider 稳定 key。数据库、UI 和服务层都使用这些值交换 provider 身份。</summary>
public static class GitProviderKeys
{
    /// <summary>GitHub.com 或 GitHub Enterprise。</summary>
    public const string GitHub = "github";

    /// <summary>GitLab.com 或自建 GitLab。</summary>
    public const string GitLab = "gitlab";

    /// <summary>Gitea / Forgejo 实例。</summary>
    public const string Gitea = "gitea";

    /// <summary>只通过标准 Git remote URL 连接的通用 Git 服务。</summary>
    public const string Generic = "generic";
}
