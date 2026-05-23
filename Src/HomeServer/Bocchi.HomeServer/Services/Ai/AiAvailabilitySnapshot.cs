namespace Bocchi.HomeServer.Services.Ai;

/// <summary>浏览器侧 AI provider 探测结果，用于 Dashboard 决定是否展示 AI 能力入口。</summary>
public sealed class AiAvailabilitySnapshot
{
    /// <summary>空探测结果；通常表示浏览器尚未完成 JS 初始化或没有可用 provider。</summary>
    public static AiAvailabilitySnapshot Empty { get; } = new();

    /// <summary>当前浏览器检测到的 AI provider 列表。</summary>
    public IReadOnlyList<AiProviderAvailability> Providers { get; init; } = [];

    /// <summary>至少存在一个可立即调用的 AI provider。</summary>
    public bool HasAvailableProvider => Providers.Any(static provider => provider.IsAvailable);
}
