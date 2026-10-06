using System.Net;

using Bocchi.HomeServer.Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bocchi.HomeServer.Tests;

/// <summary>反代场景：受信任代理的转发头参与同源判断，不受信任来源的伪造头被忽略。</summary>
public sealed class ForwardedHeadersTests
{
    private const string PublicOrigin = "https://blog.example.com";

    /// <summary>
    /// 转发后的请求是 https，Identity cookie 会带 Secure 标记；测试客户端也走 https 才会回传 cookie。
    /// Host 仍是 localhost，所以同源判断能否通过取决于 X-Forwarded-Host 是否被采信。
    /// </summary>
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    };

    [Fact]
    public async Task TrustedProxy_ForwardedHttpsHost_PassesSetupAndLogin()
    {
        using var root = new IsolatedDataRootWebApplicationFactory();
        using var factory = CreateFactory(root, remoteIp: "10.1.2.3", trustedProxies: "10.0.0.0/8");
        using var client = factory.CreateClient(ClientOptions);

        var admin = await client.SendAsync(CreateProxiedPost("/Setup/Admin", new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["displayName"] = "Bocchi Admin",
            ["email"] = string.Empty,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        admin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        admin.Headers.Location!.ToString().Should().Be("/Setup/Site");

        var complete = await client.SendAsync(CreateProxiedPost("/Setup/Complete", new Dictionary<string, string>
        {
            ["siteName"] = "Proxy Site",
            ["defaultTitle"] = "Proxy",
            ["description"] = "Behind a reverse proxy",
            ["publicBaseUrl"] = PublicOrigin + "/",
            ["copyrightNotice"] = "Copyright",
        }));
        complete.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var loginClient = factory.CreateClient(ClientOptions);
        var login = await loginClient.SendAsync(CreateProxiedPost("/Account/Login/Submit", new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.ToString().Should().NotContain("/Account/Login");
    }

    [Fact]
    public async Task UntrustedRemote_ForgedForwardedHeaders_AreIgnored()
    {
        using var root = new IsolatedDataRootWebApplicationFactory();
        using var factory = CreateFactory(root, remoteIp: "203.0.113.9", trustedProxies: null);
        using var client = factory.CreateClient(ClientOptions);

        var response = await client.SendAsync(CreateProxiedPost("/Setup/Admin", new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));

        // 转发头没被采信，请求 Host 仍是 localhost，与 Origin 不同源。
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Loopback_IsTrustedWithoutConfiguration()
    {
        using var root = new IsolatedDataRootWebApplicationFactory();
        using var factory = CreateFactory(root, remoteIp: "127.0.0.1", trustedProxies: null);
        using var client = factory.CreateClient(ClientOptions);

        var response = await client.SendAsync(CreateProxiedPost("/Setup/Admin", new Dictionary<string, string>
        {
            ["username"] = IsolatedDataRootWebApplicationFactory.AdminUserName,
            ["displayName"] = "Bocchi Admin",
            ["email"] = string.Empty,
            ["password"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
            ["confirmPassword"] = IsolatedDataRootWebApplicationFactory.AdminPassword,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public void TrustedProxies_ParsesAddressesAndNetworks()
    {
        var options = new ForwardedHeadersOptions();
        new ReverseProxyOptions { TrustedProxies = "172.18.0.0/16; 192.168.1.10, fd00::1" }.ApplyTo(options);

        options.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("172.18.0.0/16"));
        options.KnownProxies.Should().Contain([IPAddress.Parse("192.168.1.10"), IPAddress.Parse("fd00::1")]);
        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedHost);
    }

    [Fact]
    public void TrustedProxies_RejectsInvalidEntry()
    {
        var act = () => new ReverseProxyOptions { TrustedProxies = "proxy.local" }.ApplyTo(new ForwardedHeadersOptions());

        act.Should().Throw<BocchiStartupException>().WithMessage("*proxy.local*");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IsolatedDataRootWebApplicationFactory root, string remoteIp, string? trustedProxies)
        => root.WithWebHostBuilder(builder =>
        {
            if (trustedProxies is not null)
            {
                builder.UseSetting("Bocchi:ReverseProxy:TrustedProxies", trustedProxies);
            }

            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse(remoteIp))));
        });

    /// <summary>模拟浏览器经 HTTPS 反代访问：Origin 是公开地址，代理补上转发头。</summary>
    private static HttpRequestMessage CreateProxiedPost(string path, Dictionary<string, string> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("Origin", PublicOrigin);
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "blog.example.com");
        request.Headers.Add("X-Forwarded-For", "198.51.100.7");
        return request;
    }

    /// <summary>TestServer 没有真实 TCP 连接，这里在管道最前面伪造连接的远端地址。</summary>
    private sealed class RemoteIpStartupFilter(IPAddress remoteIp) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = remoteIp;
                    return nextMiddleware(context);
                });
                next(app);
            };
    }
}
