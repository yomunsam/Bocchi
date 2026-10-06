using Bocchi.HomeServer.Hosting;

namespace Bocchi.HomeServer.Maintenance;

/// <summary>备份相关配置，绑定 <c>Bocchi:Backup</c> 节。</summary>
public sealed class BackupOptions
{
    /// <summary>配置节名（环境变量前缀 <c>Bocchi__Backup__</c>）。</summary>
    public const string SectionName = "Bocchi:Backup";

    /// <summary>升级迁移前自动备份的数据库快照保留份数，超出后删除最旧的。</summary>
    public int PreMigrationRetainCount { get; set; } = 5;

    /// <summary>启动早期检查配置能否绑定，写错时在碰数据库之前就报错。</summary>
    public static void EnsureValid(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            _ = configuration.GetSection(SectionName).Get<BackupOptions>();
        }
        catch (InvalidOperationException ex)
        {
            throw new BocchiStartupException($"{SectionName} 配置无效：{ex.Message}");
        }
    }
}
