using System.Reflection;

namespace Bocchi.HomeServer;

/// <summary>
/// 提供 Home Server 自身的元信息（版本、构建时间等），供后台首页与诊断端点使用。
/// </summary>
public static class ServerInfo
{
    private static readonly Assembly EntryAssembly = typeof(ServerInfo).Assembly;

    /// <summary>程序集 informational 版本（包含 Git 描述符，由 SDK 注入）。</summary>
    public static string Version { get; } =
        EntryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? EntryAssembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>显示名。</summary>
    public const string DisplayName = "Bocchi Home Server";
}