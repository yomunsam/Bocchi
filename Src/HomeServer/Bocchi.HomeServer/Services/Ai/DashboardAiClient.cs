using Microsoft.JSInterop;

namespace Bocchi.HomeServer.Services.Ai;

/// <summary>Dashboard 到浏览器 AI facade 的 Blazor interop 客户端。</summary>
public sealed class DashboardAiClient(IJSRuntime js)
{
    /// <summary>读取当前浏览器暴露的 AI 接入方式；JS 不可用时返回空结果，避免阻断编辑页。</summary>
    public async ValueTask<AiAvailabilitySnapshot> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await js.InvokeAsync<AiAvailabilitySnapshot>("bocchiAi.getAvailability", cancellationToken)
                ?? AiAvailabilitySnapshot.Empty;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or JSDisconnectedException)
        {
            return AiAvailabilitySnapshot.Empty;
        }
    }

    /// <summary>向浏览器侧默认或指定 provider 发起一次文本生成。</summary>
    public async ValueTask<AiPromptResponse> PromptAsync(AiPromptRequest request, CancellationToken cancellationToken = default)
        => await js.InvokeAsync<AiPromptResponse>("bocchiAi.prompt", cancellationToken, request);

    /// <summary>创建一个浏览器侧 AI 会话；调用方必须在流程结束后销毁会话。</summary>
    public async ValueTask<AiSessionReference> CreateSessionAsync(AiSessionRequest request, CancellationToken cancellationToken = default)
        => await js.InvokeAsync<AiSessionReference>("bocchiAi.createSession", cancellationToken, request);

    /// <summary>向已创建的浏览器侧 AI 会话发送一次 prompt。</summary>
    public async ValueTask<AiPromptResponse> PromptSessionAsync(string sessionId, string prompt, CancellationToken cancellationToken = default)
        => await js.InvokeAsync<AiPromptResponse>("bocchiAi.promptSession", cancellationToken, sessionId, prompt);

    /// <summary>销毁浏览器侧 AI 会话，释放端侧模型相关资源。</summary>
    public async ValueTask DestroySessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => await js.InvokeVoidAsync("bocchiAi.destroySession", cancellationToken, sessionId);
}
