namespace Bocchi.HomeServer.Services.Ai;

/// <summary>从 Blazor 发往浏览器 AI provider 的 prompt 请求。</summary>
public sealed class AiPromptRequest
{
    /// <summary>目标 provider；为空时使用浏览器侧默认 provider。</summary>
    public string? ProviderId { get; init; }

    /// <summary>用户或功能模块提交给模型的主要 prompt。</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>可选 system prompt，用于约束一次调用的角色和输出格式。</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>可选采样温度；具体支持范围由 provider 决定。</summary>
    public double? Temperature { get; init; }

    /// <summary>可选 top-k 采样参数；具体支持范围由 provider 决定。</summary>
    public double? TopK { get; init; }

    /// <summary>预期输入语言；Chrome Prompt API 当前只接受它支持的语言代码。</summary>
    public IReadOnlyList<string> ExpectedInputLanguages { get; init; } = [];

    /// <summary>预期输出语言；Chrome Prompt API 用它选择合适的安全与质量策略。</summary>
    public IReadOnlyList<string> ExpectedOutputLanguages { get; init; } = [];
}
