using System.Text.Json.Serialization;

namespace Bocchi.GeneratorContract;

/// <summary>
/// Theme 输入 JSON 的统一信封。详见 <c>Docs/Milestones/M3/M3.md §3.5</c>。
/// </summary>
/// <typeparam name="T">主体数据类型。</typeparam>
public sealed record InputEnvelope<T>
{
    /// <summary>JSON Schema URI（来自 <see cref="ContractSchemaIds"/>）。序列化为 <c>$schema</c>。</summary>
    [JsonPropertyName("$schema")]
    [JsonPropertyOrder(-3)]
    public required string Schema { get; init; }

    /// <summary>Theme Contract 版本（与 <see cref="ThemeContractVersion.Current"/> 一致）。</summary>
    [JsonPropertyOrder(-2)]
    public required string ContractVersion { get; init; }

    /// <summary>本次生成的 UTC 时间戳。</summary>
    [JsonPropertyOrder(-1)]
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>主体数据。</summary>
    [JsonPropertyOrder(0)]
    public required T Data { get; init; }
}