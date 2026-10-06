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

    /// <summary>Data Protection 的固定应用名。默认值取决于安装路径，固定后换目录也能解密旧数据。</summary>
    public const string DataProtectionApplicationName = "Bocchi.HomeServer";
}