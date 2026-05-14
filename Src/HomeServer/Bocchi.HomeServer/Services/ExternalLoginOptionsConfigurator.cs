using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;

using Bocchi.HomeServer.Data;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bocchi.HomeServer.Services;

/// <summary>
/// 把数据库中的第三方登录设置投影到 ASP.NET Core authentication options。
/// </summary>
public sealed class ExternalLoginOptionsConfigurator :
    IConfigureNamedOptions<OAuthOptions>,
    IConfigureNamedOptions<OpenIdConnectOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDataProtector _protector;

    /// <summary>构造 options 配置器。</summary>
    public ExternalLoginOptionsConfigurator(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection)
    {
        _scopeFactory = scopeFactory;
        _protector = protection.CreateProtector("Bocchi.HomeServer.ExternalLoginProviderSettings.v1");
    }

    /// <summary>配置 GitHub OAuth options。</summary>
    public void Configure(string? name, OAuthOptions options)
    {
        if (!string.Equals(name, "github", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var provider = LoadProvider("github");
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
        options.TokenEndpoint = "https://github.com/login/oauth/access_token";
        options.UserInformationEndpoint = "https://api.github.com/user";
        options.CallbackPath = provider?.CallbackPath ?? "/signin-github";
        options.ClientId = string.IsNullOrWhiteSpace(provider?.ClientId) ? "not-configured" : provider!.ClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(provider?.ProtectedClientSecret) ? "not-configured" : Unprotect(provider.ProtectedClientSecret);
        options.SaveTokens = true;
        options.Scope.Clear();
        options.Scope.Add("read:user");
        options.Scope.Add("user:email");
        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
        options.ClaimActions.MapJsonKey("urn:github:login", "login");
        options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
        options.Events.OnCreatingTicket = PopulateGitHubClaimsAsync;
    }

    /// <summary>无名配置入口，ASP.NET Core options 管线需要该方法。</summary>
    public void Configure(OAuthOptions options) => Configure(Options.DefaultName, options);

    /// <summary>配置通用 OIDC options。</summary>
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (!string.Equals(name, "oidc", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var provider = LoadProvider("oidc");
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.Authority = string.IsNullOrWhiteSpace(provider?.Authority) ? "https://invalid.local" : provider!.Authority;
        options.CallbackPath = provider?.CallbackPath ?? "/signin-oidc-custom";
        options.ClientId = string.IsNullOrWhiteSpace(provider?.ClientId) ? "not-configured" : provider!.ClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(provider?.ProtectedClientSecret) ? "not-configured" : Unprotect(provider.ProtectedClientSecret);
        options.ResponseType = provider?.ResponseType ?? "code";
        options.UsePkce = provider?.UsePkce ?? true;
        options.SaveTokens = true;
        options.Scope.Clear();
        foreach (var scope in (provider?.Scopes ?? "openid profile email").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            options.Scope.Add(scope);
        }

        if (!string.IsNullOrWhiteSpace(provider?.NameClaimType))
        {
            options.TokenValidationParameters.NameClaimType = provider.NameClaimType;
        }

        if (!string.IsNullOrWhiteSpace(provider?.EmailClaimType))
        {
            options.ClaimActions.MapUniqueJsonKey(ClaimTypes.Email, provider.EmailClaimType);
        }
    }

    /// <summary>无名配置入口，ASP.NET Core options 管线需要该方法。</summary>
    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);

    private ExternalLoginProviderSettings? LoadProvider(string providerKey)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BocchiDbContext>();
        return db.ExternalLoginProviders
            .AsNoTracking()
            .FirstOrDefault(x => x.ProviderKey == providerKey);
    }

    private string Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        return _protector.Unprotect(protectedValue);
    }

    private static async Task PopulateGitHubClaimsAsync(OAuthCreatingTicketContext context)
    {
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
        userRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        userRequest.Headers.UserAgent.ParseAdd("Bocchi-HomeServer");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

        using var userResponse = await context.Backchannel.SendAsync(
            userRequest,
            HttpCompletionOption.ResponseHeadersRead,
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        userResponse.EnsureSuccessStatusCode();

        using var user = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted).ConfigureAwait(false));
        context.RunClaimActions(user.RootElement);

        if (!context.Identity!.HasClaim(c => c.Type == ClaimTypes.Email))
        {
            var email = await FindGitHubPrimaryEmailAsync(context).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(email))
            {
                context.Identity.AddClaim(new Claim(ClaimTypes.Email, email));
            }
        }
    }

    private static async Task<string?> FindGitHubPrimaryEmailAsync(OAuthCreatingTicketContext context)
    {
        using var emailRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
        emailRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        emailRequest.Headers.UserAgent.ParseAdd("Bocchi-HomeServer");
        emailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

        using var emailResponse = await context.Backchannel.SendAsync(
            emailRequest,
            HttpCompletionOption.ResponseHeadersRead,
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (!emailResponse.IsSuccessStatusCode)
        {
            return null;
        }

        using var emails = JsonDocument.Parse(await emailResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted).ConfigureAwait(false));
        foreach (var item in emails.RootElement.EnumerateArray())
        {
            var isPrimary = item.TryGetProperty("primary", out var primary) && primary.GetBoolean();
            var verified = item.TryGetProperty("verified", out var verifiedProp) && verifiedProp.GetBoolean();
            if (isPrimary && verified && item.TryGetProperty("email", out var email))
            {
                return email.GetString();
            }
        }

        return null;
    }
}
