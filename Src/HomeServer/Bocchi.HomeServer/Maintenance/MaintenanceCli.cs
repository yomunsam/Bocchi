using Bocchi.Workspace;

namespace Bocchi.HomeServer.Maintenance;

/// <summary>
/// 不启动 Web 服务的维护命令。在应用迁移和初始化 DataRoot 之前执行，restore 才不会被启动流程抢先改写数据：<br/>
/// <c>Bocchi.HomeServer backup [--output=&lt;file.zip&gt;]</c><br/>
/// <c>Bocchi.HomeServer restore &lt;file.zip&gt; [--force]</c>
/// </summary>
public static class MaintenanceCli
{
    /// <summary>判断参数是否为维护命令。</summary>
    public static bool IsMaintenanceCommand(string[] args)
        => args.Length > 0 && args[0] is "backup" or "restore";

    /// <summary>执行维护命令，返回进程退出码。</summary>
    public static int Run(string[] args, BocchiDataLayout layout, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        var service = new DataBackupService(layout, TimeProvider.System);
        try
        {
            if (args[0] == "backup")
            {
                var file = args.Skip(1).FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?["--output=".Length..];
                output.WriteLine($"备份已写入：{service.CreateBackup(file)}");
                return 0;
            }

            var source = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
            if (source is null)
            {
                output.WriteLine("用法：Bocchi.HomeServer restore <备份.zip> [--force]");
                return 2;
            }

            var safety = service.Restore(source, force: args.Contains("--force", StringComparer.Ordinal));
            if (safety is not null)
            {
                output.WriteLine($"恢复前的数据已另存为：{safety}");
            }

            output.WriteLine($"已从 {source} 恢复到 {layout.DataRoot}。启动服务后会按需应用数据库迁移。");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"失败：{ex.Message}");
            return 1;
        }
    }
}
