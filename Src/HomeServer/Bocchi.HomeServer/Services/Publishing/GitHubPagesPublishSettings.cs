using System.Text.Json;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>GitHub Pages 发布方案的非敏感配置。</summary>
public sealed record GitHubPagesPublishConfiguration
{
    /// <summary>配置 JSON 序列化选项，和 Dashboard 其他 JSON object 保持 camelCase。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>GitHub repository owner。</summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary>GitHub repository name，不包含 owner。</summary>
    public string Repository { get; init; } = string.Empty;

    /// <summary>承载 Pages 静态输出的 branch。</summary>
    public string Branch { get; init; } = "gh-pages";

    /// <summary>是否在 push 后确保 GitHub Pages source 指向该 branch 根目录。</summary>
    public bool EnsurePagesSource { get; init; } = true;

    /// <summary>从 JSON 读取配置，并补齐默认值。</summary>
    public static GitHubPagesPublishConfiguration FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new GitHubPagesPublishConfiguration();
        }

        return JsonSerializer.Deserialize<GitHubPagesPublishConfiguration>(json, JsonOptions)
            ?? new GitHubPagesPublishConfiguration();
    }

    /// <summary>序列化为 PublishPlan 使用的 JSON object。</summary>
    public string ToJson()
        => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>返回经过 trim 和默认值处理后的配置。</summary>
    public GitHubPagesPublishConfiguration Normalize()
        => this with
        {
            Owner = Owner.Trim(),
            Repository = Repository.Trim(),
            Branch = string.IsNullOrWhiteSpace(Branch) ? "gh-pages" : Branch.Trim(),
        };
}

/// <summary>GitHub Pages 发布方案的敏感凭据。</summary>
public sealed record GitHubPagesPublishCredential
{
    /// <summary>凭据 JSON 序列化选项；只用于保护前后的短暂转换。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>GitHub token；需要 Contents write，自动配置 Pages source 时还需要 Pages/Admin 权限。</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>从 JSON 读取凭据。</summary>
    public static GitHubPagesPublishCredential FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new GitHubPagesPublishCredential();
        }

        return JsonSerializer.Deserialize<GitHubPagesPublishCredential>(json, JsonOptions)
            ?? new GitHubPagesPublishCredential();
    }

    /// <summary>序列化为 PublishPlan 保存的凭据 JSON。</summary>
    public string ToJson()
        => JsonSerializer.Serialize(this, JsonOptions);
}
