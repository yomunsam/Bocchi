namespace Bocchi.HomeServer.Maintenance;

/// <summary>备份相关配置，绑定 <c>Bocchi:Backup</c> 节。</summary>
public sealed class BackupOptions
{
    /// <summary>配置节名（环境变量前缀 <c>Bocchi__Backup__</c>）。</summary>
    public const string SectionName = "Bocchi:Backup";

    /// <summary>升级迁移前自动备份的数据库快照保留份数，超出后删除最旧的。</summary>
    public int PreMigrationRetainCount { get; set; } = 5;
}
