using System.Text.Json;

namespace Bocchi.HomeServer.Services.Publishing;

/// <summary>本地目录发布方案的非敏感配置。</summary>
public sealed record LocalDirectoryPublishConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>要把静态站点镜像到的绝对目录。可写在 DataRoot 之外。</summary>
    public string TargetDirectory { get; init; } = string.Empty;

    /// <summary>
    /// 目标目录已有文件且没有 Bocchi 标记时，是否仍允许清空后镜像。
    /// 默认 false，避免误清空别人的目录。
    /// </summary>
    public bool AllowUnmarkedTarget { get; init; }

    public static LocalDirectoryPublishConfiguration FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LocalDirectoryPublishConfiguration();
        }

        return JsonSerializer.Deserialize<LocalDirectoryPublishConfiguration>(json, JsonOptions)
            ?? new LocalDirectoryPublishConfiguration();
    }

    public string ToJson()
        => JsonSerializer.Serialize(this, JsonOptions);

    public LocalDirectoryPublishConfiguration Normalize()
        => this with
        {
            TargetDirectory = TargetDirectory.Trim(),
        };
}
