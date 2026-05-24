using Bocchi.HomeServer.Services;

namespace Bocchi.HomeServer.Security;

/// <summary>
/// Setup 完成前的访问闸门：只放行 Setup flow、健康检查和后台静态资源。
/// </summary>
public sealed class SetupGateMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>构造 Setup 闸门中间件。</summary>
    public SetupGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>检查当前请求是否需要先完成 Setup。</summary>
    public async Task InvokeAsync(HttpContext context, HomeServerSetupService setup)
    {
        if (IsAllowedBeforeSetup(context.Request.Path)
            || await setup.IsSetupCompleteAsync(context.RequestAborted).ConfigureAwait(false))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.Redirect("/Setup");
    }

    private static bool IsAllowedBeforeSetup(PathString path)
    {
        var value = path.Value ?? "/";
        if (value.Equals("/Setup", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/Setup/", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HomeServerStaticAssetPaths.IsHomeServerAsset(path);
    }
}
