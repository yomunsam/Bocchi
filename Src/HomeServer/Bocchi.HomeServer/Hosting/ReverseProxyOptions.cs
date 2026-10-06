using System.Net;

using Microsoft.AspNetCore.HttpOverrides;

namespace Bocchi.HomeServer.Hosting;

/// <summary>
/// 反向代理配置。只有来自受信任代理的连接，其 X-Forwarded-For/Proto/Host 才会被采信，
/// 否则任何人都能伪造 scheme/host 绕过同源守卫或污染 OAuth 回调地址。
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>配置节名（环境变量前缀 <c>Bocchi__ReverseProxy__</c>）。</summary>
    public const string SectionName = "Bocchi:ReverseProxy";

    /// <summary>
    /// 额外受信任的代理地址或网段，逗号/分号/空白分隔，例如 <c>172.18.0.0/16, 192.168.1.10</c>。
    /// 回环地址始终受信任：能从本机回环连进来的进程本来就在这台机器上，同机反代无需额外配置。
    /// </summary>
    public string? TrustedProxies { get; set; }

    /// <summary>把配置应用到 ASP.NET Core 的 <see cref="ForwardedHeadersOptions"/>。</summary>
    public void ApplyTo(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;

        // 框架默认已包含 127.0.0.0/8 与 ::1，这里只追加用户配置。
        var entries = (TrustedProxies ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            if (entry.Contains('/', StringComparison.Ordinal) && System.Net.IPNetwork.TryParse(entry, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                throw new BocchiStartupException(
                    $"Bocchi:ReverseProxy:TrustedProxies 中的 '{entry}' 不是合法的 IP 地址或 CIDR 网段。");
            }
        }
    }
}
