using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace Bocchi.Home.WebHost.Components.Identity;

internal sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    public const string StatusCookieName = "Identity.StatusManage";

    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        MaxAge = TimeSpan.FromMinutes(5)
    };

    [DoesNotReturn]
    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // 防止开放重定向攻击（Open Redirect Attack）
        if(!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }
        
        navigationManager.NavigateTo(uri);
        throw new InvalidOperationException($"{nameof(IdentityRedirectManager)} can only be used during static rendering.");
    
        /*
            以上写法来自AspNetCore预设模板，原理是这样的：
            Account相关的页面全部是SSR渲染模式，走REST方式和服务器通讯，在这种情况下，navigationManager.NavigateTo会抛出一个特殊的NavigationException
            这个异常会被Blazor框架捕获并处理为重定向.
            通常情况下绝对不会执行到InvalidOperationException这一步
        */
    }

}