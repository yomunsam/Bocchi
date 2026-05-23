namespace Bocchi.HomeServer.Services.Ai;

/// <summary>单个 AI 接入方式的浏览器侧可用性。</summary>
public sealed class AiProviderAvailability
{
    /// <summary>provider 的稳定标识，例如 chrome-built-in。</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>用于 UI 或调试日志的人类可读名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>provider 状态；字符串与浏览器 API 保持一致，避免频繁适配实验性枚举。</summary>
    public string Status { get; init; } = AiProviderStatuses.Unavailable;

    /// <summary>状态异常或不可用时的辅助说明。</summary>
    public string? Reason { get; init; }

    /// <summary>当前 provider 是否已经可以直接响应 prompt。</summary>
    public bool IsAvailable => string.Equals(Status, AiProviderStatuses.Available, StringComparison.Ordinal);
}
