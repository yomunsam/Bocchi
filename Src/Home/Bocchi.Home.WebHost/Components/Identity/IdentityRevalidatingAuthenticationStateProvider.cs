
using System.Security.Claims;
using Bocchi.Home.Core.Entities.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Bocchi.Home.WebHost.Components.Identity;

internal sealed class IdentityRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOptions> options
    )
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /* 
        适用于Blazor Server模式的身份验证状态提供程序
        - 每30分钟对已连接的用户的安全戳进行重新验证
        - 防止已注销或被禁用的用户继续访问
    */
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, 
        CancellationToken cancellationToken
        )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<BocchiUserEntity>>();
        return await ValidateSecurityStampAsync(userManager, authenticationState.User);
    }


    private async Task<bool> ValidateSecurityStampAsync(UserManager<BocchiUserEntity> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }
        else if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }
        else
        {
            var principalStamp = principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
            var userStamp = await userManager.GetSecurityStampAsync(user);
            return principalStamp == userStamp;
        }
    }

}