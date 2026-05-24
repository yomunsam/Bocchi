using Microsoft.AspNetCore.Authentication;

using Bocchi.HomeServer.Data;

namespace Bocchi.HomeServer.Security;

/// <summary>
/// Admin path 的认证与角色闸门。组件路由不再整站 RequireAuthorization，因此这里按路径提供 HTTP 级重定向语义。
/// </summary>
public sealed class AdminGateMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>构造 Admin 闸门中间件。</summary>
    public AdminGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>拦截 /Admin 路径：匿名用户 challenge，非 Admin 用户 forbid。</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync().ConfigureAwait(false);
            return;
        }

        if (!context.User.IsInRole(BocchiRoleNames.Admin))
        {
            await context.ForbidAsync().ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }
}
