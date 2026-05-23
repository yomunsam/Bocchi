namespace Bocchi.HomeServer.Services.Ai;

/// <summary>浏览器 AI provider 返回的一次文本生成结果。</summary>
public sealed class AiPromptResponse
{
    /// <summary>实际响应请求的 provider 标识。</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>模型生成的文本内容。</summary>
    public string Text { get; init; } = string.Empty;
}
