namespace Bocchi.HomeServer.Services.Ai;

/// <summary>浏览器侧 AI 会话引用；Home Server 只保存句柄，不持有模型对象本身。</summary>
public sealed class AiSessionReference
{
    /// <summary>浏览器 facade 分配的会话标识。</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>实际创建会话的 provider 标识。</summary>
    public string ProviderId { get; init; } = string.Empty;
}
