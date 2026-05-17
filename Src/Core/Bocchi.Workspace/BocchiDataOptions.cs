namespace Bocchi.Workspace;

/// <summary>
/// Bocchi 持久化数据运行选项。从 <c>appsettings.json</c> 的 <c>Bocchi</c> 节绑定。
/// </summary>
public sealed class BocchiDataOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Bocchi";

    /// <summary>
    /// DataRoot 的绝对或相对路径。相对路径相对于宿主传入的 base path 解析。
    /// 留空时回退到宿主传入 base path 下的 <c>data/</c>。
    /// </summary>
    public string? DataRoot { get; set; }

    /// <summary>
    /// 启动时是否自动初始化 DataRoot 与内容 workspace（创建缺失目录、写入默认 README/.gitignore/site.yaml 等）。
    /// 默认 <c>true</c>，符合"开箱即用"原则。
    /// </summary>
    public bool AutoInitialize { get; set; } = true;

    /// <summary>启动时是否自动迁移 SQLite schema。默认 <c>true</c>。</summary>
    public bool AutoMigrateSchema { get; set; } = true;
}
