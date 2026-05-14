namespace Bocchi.Workspace;

/// <summary>
/// 工作区运行选项。从 <c>appsettings.json</c> 的 <c>Bocchi</c> 节绑定。
/// </summary>
public sealed class WorkspaceOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Bocchi";

    /// <summary>
    /// 工作区根目录的绝对或相对路径。相对路径相对于 <see cref="WorkspaceRootBase"/>（由宿主提供）解析。
    /// 留空时回退到 <c>&lt;ContentRoot&gt;/workspace</c>。
    /// </summary>
    public string? WorkspaceRoot { get; set; }

    /// <summary>
    /// 启动时是否自动初始化工作区（创建缺失的目录、写入默认 README/.gitignore/site.yaml 等）。
    /// 默认 <c>true</c>，符合"开箱即用"原则。
    /// </summary>
    public bool AutoInitialize { get; set; } = true;

    /// <summary>启动时是否自动迁移 SQLite schema。默认 <c>true</c>。</summary>
    public bool AutoMigrateSchema { get; set; } = true;
}
